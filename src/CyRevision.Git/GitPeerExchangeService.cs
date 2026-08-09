using System.Security.Cryptography;
using System.Text.Json;
using CyRevision.Security;

namespace CyRevision.Git;

public sealed record GitPeerBundleManifest(
    Guid TransactionId,
    Guid ProjectId,
    DeviceIdentity Author,
    DateTimeOffset CreatedAt,
    string BundleFileName,
    string BundleHash,
    IReadOnlyDictionary<string, string> Branches);

public sealed record SignedGitPeerBundle(GitPeerBundleManifest Manifest, string Signature);

public sealed record GitPeerExchangeResult(
    Guid? ExportedTransactionId,
    int ImportedTransactions,
    int ImportedLfsObjects,
    IReadOnlyList<string> UpdatedRemoteBranches);

public interface IGitPeerExchangeService
{
    Task<Guid?> ExportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        IDeviceIdentityStore identity,
        CancellationToken cancellationToken = default);

    Task<GitPeerExchangeResult> ImportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId = null,
        CancellationToken cancellationToken = default);
}

public sealed class GitPeerExchangeService : IGitPeerExchangeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ProcessRunner _processRunner = new();

    public async Task<Guid?> ExportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        IDeviceIdentityStore identity,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot = Path.GetFullPath(repositoryPath);
        ProcessResult head = await RunGitAsync(repositoryRoot, ["rev-parse", "--verify", "HEAD"], cancellationToken);
        if (!head.Succeeded)
        {
            return null;
        }

        string exchangeRoot = Path.GetFullPath(exchangePath);
        string stagingRoot = Path.Combine(exchangeRoot, ".cyrevision-staging");
        string transactionsRoot = Path.Combine(exchangeRoot, "transactions");
        Directory.CreateDirectory(exchangeRoot);
        EnsureIgnoreFile(exchangeRoot);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(transactionsRoot);

        Guid transactionId = Guid.NewGuid();
        string stagingTransaction = Path.Combine(stagingRoot, transactionId.ToString("N"));
        string publishedTransaction = Path.Combine(transactionsRoot, transactionId.ToString("N"));
        Directory.CreateDirectory(stagingTransaction);
        try
        {
            const string bundleFileName = "repository.bundle";
            string bundlePath = Path.Combine(stagingTransaction, bundleFileName);
            ProcessResult bundle = await RunGitAsync(
                repositoryRoot,
                ["bundle", "create", bundlePath, "--branches"],
                cancellationToken);
            EnsureSucceeded(bundle, "Git bundle creation failed");

            IReadOnlyDictionary<string, string> branches = await ReadBranchesAsync(repositoryRoot, cancellationToken);
            string bundleHash = await ComputeFileHashAsync(bundlePath, cancellationToken);
            GitPeerBundleManifest manifest = new(
                transactionId,
                projectId,
                identity.Identity,
                DateTimeOffset.UtcNow,
                bundleFileName,
                bundleHash,
                branches);
            byte[] canonicalManifest = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            SignedGitPeerBundle signed = new(manifest, identity.Sign(canonicalManifest));
            await File.WriteAllBytesAsync(
                Path.Combine(stagingTransaction, "transaction.json"),
                JsonSerializer.SerializeToUtf8Bytes(signed, JsonOptions),
                cancellationToken);

            await ExportLfsObjectsAsync(repositoryRoot, exchangeRoot, cancellationToken);
            Directory.Move(stagingTransaction, publishedTransaction);
            return transactionId;
        }
        finally
        {
            if (Directory.Exists(stagingTransaction))
            {
                Directory.Delete(stagingTransaction, true);
            }
        }
    }

    public async Task<GitPeerExchangeResult> ImportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot = Path.GetFullPath(repositoryPath);
        string exchangeRoot = Path.GetFullPath(exchangePath);
        string stateRoot = Path.GetFullPath(localStatePath);
        Directory.CreateDirectory(stateRoot);
        HashSet<Guid> processed = await ReadProcessedTransactionsAsync(stateRoot, cancellationToken);
        Dictionary<Guid, DeviceIdentity> authorized = authorizedDevices
            .GroupBy(device => device.DeviceId)
            .ToDictionary(group => group.Key, group => group.First());
        List<string> updatedBranches = [];
        int importedTransactions = 0;

        string transactionsRoot = Path.Combine(exchangeRoot, "transactions");
        if (Directory.Exists(transactionsRoot))
        {
            foreach (string transactionDirectory in Directory.EnumerateDirectories(transactionsRoot).OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string transactionPath = Path.Combine(transactionDirectory, "transaction.json");
                if (!File.Exists(transactionPath))
                {
                    continue;
                }

                SignedGitPeerBundle signed = JsonSerializer.Deserialize<SignedGitPeerBundle>(
                                                 await File.ReadAllBytesAsync(transactionPath, cancellationToken),
                                                 JsonOptions)
                                             ?? throw new InvalidDataException($"Invalid Git transaction: {transactionPath}");
                GitPeerBundleManifest manifest = signed.Manifest;
                if (processed.Contains(manifest.TransactionId))
                {
                    continue;
                }

                if (localDeviceId is not null && manifest.Author.DeviceId == localDeviceId)
                {
                    processed.Add(manifest.TransactionId);
                    continue;
                }

                if (manifest.ProjectId != projectId ||
                    !authorized.TryGetValue(manifest.Author.DeviceId, out DeviceIdentity? authorizedDevice) ||
                    !string.Equals(authorizedDevice.SigningPublicKey, manifest.Author.SigningPublicKey, StringComparison.Ordinal))
                {
                    continue;
                }

                byte[] canonicalManifest = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                if (!PeerExchangeCodec.VerifySignature(authorizedDevice, canonicalManifest, signed.Signature))
                {
                    throw new CryptographicException($"Git transaction {manifest.TransactionId} has an invalid signature.");
                }

                string bundlePath = ResolveContainedPath(transactionDirectory, manifest.BundleFileName);
                if (!File.Exists(bundlePath) ||
                    !string.Equals(await ComputeFileHashAsync(bundlePath, cancellationToken), manifest.BundleHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Git transaction {manifest.TransactionId} has an invalid bundle hash.");
                }

                ProcessResult verify = await RunGitAsync(repositoryRoot, ["bundle", "verify", bundlePath], cancellationToken);
                EnsureSucceeded(verify, "Git bundle verification failed");
                string authorRef = SanitizeRefComponent(manifest.Author.DeviceId.ToString("N")[..12]);
                ProcessResult fetch = await RunGitAsync(
                    repositoryRoot,
                    ["fetch", bundlePath, $"+refs/heads/*:refs/remotes/cyrevision/{authorRef}/*"],
                    cancellationToken);
                EnsureSucceeded(fetch, "Git bundle import failed");
                updatedBranches.AddRange(manifest.Branches.Keys.Select(branch => $"cyrevision/{authorRef}/{branch}"));
                processed.Add(manifest.TransactionId);
                importedTransactions++;
            }
        }

        int importedLfs = await ImportLfsObjectsAsync(repositoryRoot, exchangeRoot, cancellationToken);
        await WriteProcessedTransactionsAsync(stateRoot, processed, cancellationToken);
        return new GitPeerExchangeResult(null, importedTransactions, importedLfs, updatedBranches);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitAsync(
            repositoryPath,
            ["for-each-ref", "--format=%(refname:strip=2)%00%(objectname)", "refs/heads"],
            cancellationToken);
        EnsureSucceeded(result, "Unable to enumerate Git branches");
        Dictionary<string, string> branches = new(StringComparer.Ordinal);
        foreach (string line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\0');
            if (fields.Length == 2)
            {
                branches[fields[0]] = fields[1];
            }
        }

        return branches;
    }

    private async Task ExportLfsObjectsAsync(string repositoryPath, string exchangeRoot, CancellationToken cancellationToken)
    {
        ProcessResult lfs = await RunGitAsync(repositoryPath, ["lfs", "ls-files", "--all", "-l"], cancellationToken);
        if (!lfs.Succeeded)
        {
            return;
        }

        string gitDirectory = await GetGitDirectoryAsync(repositoryPath, cancellationToken);
        foreach (string line in lfs.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string hash = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (!IsSha256(hash))
            {
                continue;
            }

            string source = Path.Combine(gitDirectory, "lfs", "objects", hash[..2], hash.Substring(2, 2), hash);
            string destination = Path.Combine(exchangeRoot, "lfs", "objects", hash[..2], hash.Substring(2, 2), hash);
            if (File.Exists(source) &&
                !File.Exists(destination) &&
                string.Equals(hash, await ComputeFileHashAsync(source, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }
        }
    }

    private async Task<int> ImportLfsObjectsAsync(string repositoryPath, string exchangeRoot, CancellationToken cancellationToken)
    {
        string sourceRoot = Path.Combine(exchangeRoot, "lfs", "objects");
        if (!Directory.Exists(sourceRoot))
        {
            return 0;
        }

        string gitDirectory = await GetGitDirectoryAsync(repositoryPath, cancellationToken);
        int imported = 0;
        foreach (string source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string hash = Path.GetFileName(source);
            if (!IsSha256(hash) ||
                !string.Equals(hash, await ComputeFileHashAsync(source, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(gitDirectory, "lfs", "objects", hash[..2], hash.Substring(2, 2), hash);
            if (!File.Exists(destination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                imported++;
            }
        }

        return imported;
    }

    private async Task<string> GetGitDirectoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitAsync(repositoryPath, ["rev-parse", "--git-dir"], cancellationToken);
        EnsureSucceeded(result, "Unable to locate the Git directory");
        string gitDirectory = result.StandardOutput.Trim();
        return Path.GetFullPath(Path.IsPathRooted(gitDirectory) ? gitDirectory : Path.Combine(repositoryPath, gitDirectory));
    }

    private Task<ProcessResult> RunGitAsync(
        string repositoryPath,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync("git", arguments, repositoryPath, cancellationToken);

    private static void EnsureSucceeded(ProcessResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new GitOperationException($"{operation}: {result.StandardError.Trim()}");
        }
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task<HashSet<Guid>> ReadProcessedTransactionsAsync(string stateRoot, CancellationToken cancellationToken)
    {
        string path = Path.Combine(stateRoot, "processed-transactions.json");
        if (!File.Exists(path))
        {
            return [];
        }

        Guid[]? values = JsonSerializer.Deserialize<Guid[]>(await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
        return values?.ToHashSet() ?? [];
    }

    private static async Task WriteProcessedTransactionsAsync(
        string stateRoot,
        HashSet<Guid> processed,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(stateRoot, "processed-transactions.json");
        string temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(
            temporaryPath,
            JsonSerializer.SerializeToUtf8Bytes(processed.OrderBy(id => id).ToArray(), JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private static void EnsureIgnoreFile(string exchangeRoot)
    {
        string ignorePath = Path.Combine(exchangeRoot, ".stignore");
        const string rule = "(?d).cyrevision-staging\n";
        if (!File.Exists(ignorePath) || !File.ReadAllText(ignorePath).Contains(".cyrevision-staging", StringComparison.Ordinal))
        {
            File.AppendAllText(ignorePath, rule);
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static string SanitizeRefComponent(string value) =>
        new(value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());

    private static string ResolveContainedPath(string root, string relativePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The transaction references a file outside its directory.");
        }

        return path;
    }
}
