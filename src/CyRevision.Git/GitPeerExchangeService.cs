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
    IReadOnlyList<string> UpdatedRemoteBranches,
    int ResumedLfsObjects = 0,
    int DeferredLfsObjects = 0,
    long ImportedLfsBytes = 0,
    int AvailablePeerLfsObjects = 0,
    int PublishedLfsObjects = 0,
    long PublishedLfsBytes = 0,
    int FulfilledLfsRequests = 0);

public interface IGitPeerExchangeService
{
    Task<Guid?> ExportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        IDeviceIdentityStore identity,
        CancellationToken cancellationToken = default);

    Task<GitPeerExportResult> ExportDetailedAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        IDeviceIdentityStore identity,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        GitPeerExchangeOptions options,
        CancellationToken cancellationToken = default);

    Task<GitPeerExchangeResult> ImportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId = null,
        CancellationToken cancellationToken = default);

    Task<GitPeerExchangeResult> ImportDetailedAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId,
        GitPeerExchangeOptions options,
        CancellationToken cancellationToken = default);

    Task<Guid> RequestLfsObjectAsync(
        Guid projectId,
        string exchangePath,
        IDeviceIdentityStore identity,
        string oidSha256,
        string reason,
        CancellationToken cancellationToken = default);

    Task<PeerLfsAvailabilityCache> GetCachedLfsAvailabilityAsync(
        string localStatePath,
        Guid projectId,
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
        CancellationToken cancellationToken = default) =>
        (await ExportDetailedAsync(
            projectId,
            repositoryPath,
            exchangePath,
            identity,
            [identity.Identity],
            GitPeerExchangeOptions.Default,
            cancellationToken)).TransactionId;

    public async Task<GitPeerExportResult> ExportDetailedAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        IDeviceIdentityStore identity,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        GitPeerExchangeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        options.Validate();
        string repositoryRoot = Path.GetFullPath(repositoryPath);
        ProcessResult head = await RunGitAsync(repositoryRoot, ["rev-parse", "--verify", "HEAD"], cancellationToken);
        if (!head.Succeeded)
        {
            return new GitPeerExportResult(null, 0, 0, 0, 0, 0, 0);
        }

        string exchangeRoot = Path.GetFullPath(exchangePath);
        string stagingRoot = Path.Combine(exchangeRoot, ".cyrevision-staging");
        string transactionsRoot = Path.Combine(exchangeRoot, "transactions");
        Directory.CreateDirectory(exchangeRoot);
        EnsureIgnoreFile(exchangeRoot);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(transactionsRoot);

        Dictionary<Guid, DeviceIdentity> authorized = CreateAuthorizedDeviceMap(authorizedDevices, identity.Identity);
        IReadOnlyList<VerifiedLfsRequest> requests = await ReadVerifiedRequestsAsync(
            projectId,
            exchangeRoot,
            authorized,
            cancellationToken);
        IReadOnlyList<LocalLfsObject> localObjects = await ReadLocalLfsObjectsAsync(repositoryRoot, cancellationToken);
        HashSet<string> requestedOids = requests.Select(request => request.Request.OidSha256)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<LocalLfsObject> classifiedObjects = ClassifyObjects(localObjects, requestedOids, options);
        IReadOnlyList<LocalLfsObject> prioritizedObjects = SelectObjectsForTransfer(classifiedObjects, requestedOids, options);
        TransferBatchResult publish = await PublishLfsObjectsAsync(
            prioritizedObjects,
            exchangeRoot,
            options.MaximumTransferBytes,
            cancellationToken);
        HashSet<string> publishedOids = publish.CompletedOids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await PublishInventoryAsync(
            projectId,
            exchangeRoot,
            identity,
            classifiedObjects.Select(item => new PeerLfsInventoryObject(
                item.OidSha256,
                item.Size,
                item.Priority,
                item.Paths,
                publishedOids.Contains(item.OidSha256) || IsPublishedObjectPresent(exchangeRoot, item.OidSha256)))
                .ToArray(),
            cancellationToken);

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
            SignedGitPeerBundle signed = new(manifest, identity.Sign(CanonicalBytes(manifest)));
            await WriteJsonAtomicallyAsync(Path.Combine(stagingTransaction, "transaction.json"), signed, cancellationToken);
            Directory.Move(stagingTransaction, publishedTransaction);
        }
        finally
        {
            if (Directory.Exists(stagingTransaction))
            {
                Directory.Delete(stagingTransaction, true);
            }
        }

        int fulfilledRequests = requests.Count(request => publishedOids.Contains(request.Request.OidSha256));
        return new GitPeerExportResult(
            transactionId,
            publish.CopiedObjects,
            publish.ResumedObjects,
            publish.DeferredObjects,
            publish.CopiedBytes,
            fulfilledRequests,
            localObjects.Count);
    }

    public Task<GitPeerExchangeResult> ImportAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId = null,
        CancellationToken cancellationToken = default) =>
        ImportDetailedAsync(
            projectId,
            repositoryPath,
            exchangePath,
            localStatePath,
            authorizedDevices,
            localDeviceId,
            GitPeerExchangeOptions.Default with { LfsTransferMode = PeerLfsTransferMode.AllAvailable },
            cancellationToken);

    public async Task<GitPeerExchangeResult> ImportDetailedAsync(
        Guid projectId,
        string repositoryPath,
        string exchangePath,
        string localStatePath,
        IReadOnlyCollection<DeviceIdentity> authorizedDevices,
        Guid? localDeviceId,
        GitPeerExchangeOptions options,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        string repositoryRoot = Path.GetFullPath(repositoryPath);
        string exchangeRoot = Path.GetFullPath(exchangePath);
        string stateRoot = Path.GetFullPath(localStatePath);
        Directory.CreateDirectory(stateRoot);
        HashSet<Guid> processed = await ReadProcessedTransactionsAsync(stateRoot, cancellationToken);
        Dictionary<Guid, DeviceIdentity> authorized = CreateAuthorizedDeviceMap(authorizedDevices);
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

                SignedGitPeerBundle? signed;
                try
                {
                    signed = JsonSerializer.Deserialize<SignedGitPeerBundle>(
                        await File.ReadAllBytesAsync(transactionPath, cancellationToken), JsonOptions);
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    continue;
                }

                GitPeerBundleManifest manifest = signed?.Manifest
                    ?? throw new InvalidDataException($"Invalid Git transaction: {transactionPath}");
                if (processed.Contains(manifest.TransactionId))
                {
                    continue;
                }

                if (localDeviceId is not null && manifest.Author.DeviceId == localDeviceId)
                {
                    processed.Add(manifest.TransactionId);
                    continue;
                }

                if (!TryAuthorize(manifest.ProjectId, projectId, manifest.Author, authorized, out DeviceIdentity? authorizedDevice))
                {
                    continue;
                }

                if (!PeerExchangeCodec.VerifySignature(authorizedDevice, CanonicalBytes(manifest), signed.Signature))
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

        IReadOnlyList<GitPeerContentInventory> inventories = await ReadVerifiedInventoriesAsync(
            projectId,
            exchangeRoot,
            authorized,
            localDeviceId,
            cancellationToken);
        PeerLfsAvailabilityCache availability = BuildAvailabilityCache(projectId, inventories);
        await WriteJsonAtomicallyAsync(GetAvailabilityCachePath(stateRoot), availability, cancellationToken);
        HashSet<string> localRequests = await ReadLocalRequestedOidsAsync(
            projectId,
            exchangeRoot,
            localDeviceId,
            authorized,
            cancellationToken);
        LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner, "git", repositoryRoot, cancellationToken);
        TransferBatchResult importedLfs = await ImportLfsObjectsAsync(
            lfsStorage,
            exchangeRoot,
            availability,
            localRequests,
            options,
            cancellationToken);
        await WriteProcessedTransactionsAsync(stateRoot, processed, cancellationToken);
        return new GitPeerExchangeResult(
            null,
            importedTransactions,
            importedLfs.CopiedObjects,
            updatedBranches,
            importedLfs.ResumedObjects,
            importedLfs.DeferredObjects,
            importedLfs.CopiedBytes,
            availability.Objects.Count);
    }

    public async Task<Guid> RequestLfsObjectAsync(
        Guid projectId,
        string exchangePath,
        IDeviceIdentityStore identity,
        string oidSha256,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        string normalizedOid = oidSha256.Trim().ToLowerInvariant();
        if (!IsSha256(normalizedOid))
        {
            throw new ArgumentException("The LFS OID is invalid.", nameof(oidSha256));
        }

        Guid requestId = Guid.NewGuid();
        PeerLfsObjectRequest request = new(
            requestId,
            projectId,
            identity.Identity,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            normalizedOid,
            string.IsNullOrWhiteSpace(reason) ? "Requested from LFS Time Machine" : reason.Trim()[..Math.Min(200, reason.Trim().Length)]);
        SignedPeerLfsObjectRequest signed = new(request, identity.Sign(CanonicalBytes(request)));
        string requestsRoot = Path.Combine(Path.GetFullPath(exchangePath), "requests");
        Directory.CreateDirectory(requestsRoot);
        string fileName = $"{identity.Identity.DeviceId:N}-{requestId:N}.json";
        await WriteJsonAtomicallyAsync(Path.Combine(requestsRoot, fileName), signed, cancellationToken);
        return requestId;
    }

    public async Task<PeerLfsAvailabilityCache> GetCachedLfsAvailabilityAsync(
        string localStatePath,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string path = GetAvailabilityCachePath(Path.GetFullPath(localStatePath));
        if (!File.Exists(path))
        {
            return PeerLfsAvailabilityCache.Empty(projectId);
        }

        try
        {
            PeerLfsAvailabilityCache? cache = JsonSerializer.Deserialize<PeerLfsAvailabilityCache>(
                await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
            return cache?.ProjectId == projectId ? cache : PeerLfsAvailabilityCache.Empty(projectId);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return PeerLfsAvailabilityCache.Empty(projectId);
        }
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

    private async Task<IReadOnlyList<LocalLfsObject>> ReadLocalLfsObjectsAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        ProcessResult currentResult = await RunGitAsync(repositoryPath, ["lfs", "ls-files", "-l"], cancellationToken);
        ProcessResult allResult = await RunGitAsync(repositoryPath, ["lfs", "ls-files", "--all", "-l"], cancellationToken);
        if (!currentResult.Succeeded && !allResult.Succeeded)
        {
            return [];
        }

        HashSet<string> currentOids = ParseLfsList(currentResult.StandardOutput)
            .Select(item => item.Oid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<(string Oid, string Path)> ordered = ParseLfsList(currentResult.StandardOutput)
            .Concat(ParseLfsList(allResult.StandardOutput))
            .ToList();
        LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner, "git", repositoryPath, cancellationToken);
        Dictionary<string, MutableLocalLfsObject> objects = new(StringComparer.OrdinalIgnoreCase);
        int historicalIndex = 0;
        foreach ((string oid, string path) in ordered)
        {
            if (!IsSha256(oid))
            {
                continue;
            }

            string objectPath = lfsStorage.GetObjectPath(oid);
            if (!File.Exists(objectPath))
            {
                continue;
            }

            if (!objects.TryGetValue(oid, out MutableLocalLfsObject? item))
            {
                bool current = currentOids.Contains(oid);
                item = new MutableLocalLfsObject(
                    oid,
                    objectPath,
                    new FileInfo(objectPath).Length,
                    current ? PeerLfsObjectPriority.Current : PeerLfsObjectPriority.Archive,
                    current ? -1 : historicalIndex++);
                objects[oid] = item;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                item.Paths.Add(path);
            }
        }

        return objects.Values
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.HistoricalIndex)
            .ThenBy(item => item.OidSha256, StringComparer.Ordinal)
            .Select(item => item.ToRecord())
            .ToArray();
    }

    private static IReadOnlyList<LocalLfsObject> ClassifyObjects(
        IReadOnlyList<LocalLfsObject> objects,
        IReadOnlySet<string> requestedOids,
        GitPeerExchangeOptions options)
    {
        LocalLfsObject[] history = objects.Where(item => item.Priority != PeerLfsObjectPriority.Current).ToArray();
        HashSet<string> recentOids = history.Take(options.RecentHistoricalObjectCount)
            .Select(item => item.OidSha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return objects
            .Select(item => item with
            {
                Priority = requestedOids.Contains(item.OidSha256)
                    ? PeerLfsObjectPriority.Requested
                    : item.Priority == PeerLfsObjectPriority.Current
                        ? PeerLfsObjectPriority.Current
                        : recentOids.Contains(item.OidSha256)
                            ? PeerLfsObjectPriority.RecentHistory
                            : PeerLfsObjectPriority.Archive
            }).ToArray();
    }

    private static IReadOnlyList<LocalLfsObject> SelectObjectsForTransfer(
        IReadOnlyList<LocalLfsObject> objects,
        IReadOnlySet<string> requestedOids,
        GitPeerExchangeOptions options) =>
        objects
            .Where(item => requestedOids.Contains(item.OidSha256) || item.Priority switch
            {
                PeerLfsObjectPriority.Current => true,
                PeerLfsObjectPriority.RecentHistory => options.LfsTransferMode >= PeerLfsTransferMode.CurrentAndRecent,
                PeerLfsObjectPriority.Archive => options.LfsTransferMode == PeerLfsTransferMode.AllAvailable,
                _ => true
            })
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.HistoricalIndex)
            .ToArray();

    private async Task<TransferBatchResult> PublishLfsObjectsAsync(
        IReadOnlyList<LocalLfsObject> objects,
        string exchangeRoot,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        int copied = 0, resumed = 0, deferred = 0;
        long copiedBytes = 0;
        List<string> completed = [];
        foreach (LocalLfsObject item in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (copiedBytes + item.Size > maximumBytes)
            {
                deferred++;
                continue;
            }

            string destination = GetExchangeLfsObjectPath(exchangeRoot, item.OidSha256);
            try
            {
                ResumableCopyResult result = await CopyResumableVerifiedAsync(
                    item.LocalObjectPath,
                    destination,
                    item.OidSha256,
                    cancellationToken);
                completed.Add(item.OidSha256);
                if (result.Copied)
                {
                    copied++;
                    copiedBytes += result.CopiedBytes;
                    if (result.Resumed)
                    {
                        resumed++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                deferred++;
            }
        }

        return new TransferBatchResult(copied, resumed, deferred, copiedBytes, completed);
    }

    private async Task<TransferBatchResult> ImportLfsObjectsAsync(
        LfsStoragePaths lfsStorage,
        string exchangeRoot,
        PeerLfsAvailabilityCache availability,
        IReadOnlySet<string> requestedOids,
        GitPeerExchangeOptions options,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.Combine(exchangeRoot, "lfs", "objects");
        if (!Directory.Exists(sourceRoot))
        {
            return new TransferBatchResult(0, 0, 0, 0, []);
        }

        List<ImportCandidate> candidates = [];
        foreach (PeerLfsObjectAvailability available in availability.Objects)
        {
            PeerLfsAvailabilityLocation[] publishedByAuthorizedPeer = available.Peers
                .Where(peer => peer.PublishedToExchange)
                .ToArray();
            if (publishedByAuthorizedPeer.Length == 0 || !IsSha256(available.OidSha256))
            {
                continue;
            }

            string oid = available.OidSha256;
            string source = GetExchangeLfsObjectPath(exchangeRoot, oid);
            if (!File.Exists(source))
            {
                continue;
            }

            PeerLfsObjectPriority priority = requestedOids.Contains(oid)
                ? PeerLfsObjectPriority.Requested
                : publishedByAuthorizedPeer.Min(peer => peer.Priority);
            bool selected = requestedOids.Contains(oid) || priority switch
            {
                PeerLfsObjectPriority.Current => true,
                PeerLfsObjectPriority.Requested => true,
                PeerLfsObjectPriority.RecentHistory => options.LfsTransferMode >= PeerLfsTransferMode.CurrentAndRecent,
                _ => options.LfsTransferMode == PeerLfsTransferMode.AllAvailable
            };
            candidates.Add(new ImportCandidate(oid, source, new FileInfo(source).Length, priority, selected));
        }

        int copied = 0, resumed = 0, deferred = candidates.Count(candidate => !candidate.Selected);
        long copiedBytes = 0;
        List<string> completed = [];
        foreach (ImportCandidate candidate in candidates.Where(candidate => candidate.Selected)
                     .OrderBy(candidate => candidate.Priority).ThenBy(candidate => candidate.OidSha256, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (copiedBytes + candidate.Size > options.MaximumTransferBytes)
            {
                deferred++;
                continue;
            }

            string destination = lfsStorage.GetObjectPath(candidate.OidSha256);
            try
            {
                ResumableCopyResult result = await CopyResumableVerifiedAsync(
                    candidate.SourcePath,
                    destination,
                    candidate.OidSha256,
                    cancellationToken);
                completed.Add(candidate.OidSha256);
                if (result.Copied)
                {
                    copied++;
                    copiedBytes += result.CopiedBytes;
                    if (result.Resumed)
                    {
                        resumed++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                deferred++;
            }
        }

        return new TransferBatchResult(copied, resumed, deferred, copiedBytes, completed);
    }

    private async Task PublishInventoryAsync(
        Guid projectId,
        string exchangeRoot,
        IDeviceIdentityStore identity,
        IReadOnlyList<PeerLfsInventoryObject> objects,
        CancellationToken cancellationToken)
    {
        GitPeerContentInventory inventory = new(
            Guid.NewGuid(),
            projectId,
            identity.Identity,
            DateTimeOffset.UtcNow,
            objects);
        SignedGitPeerContentInventory signed = new(inventory, identity.Sign(CanonicalBytes(inventory)));
        string inventoriesRoot = Path.Combine(exchangeRoot, "inventories");
        Directory.CreateDirectory(inventoriesRoot);
        await WriteJsonAtomicallyAsync(
            Path.Combine(inventoriesRoot, identity.Identity.DeviceId.ToString("N") + ".json"),
            signed,
            cancellationToken);
    }

    private async Task<IReadOnlyList<GitPeerContentInventory>> ReadVerifiedInventoriesAsync(
        Guid projectId,
        string exchangeRoot,
        IReadOnlyDictionary<Guid, DeviceIdentity> authorized,
        Guid? localDeviceId,
        CancellationToken cancellationToken)
    {
        string inventoriesRoot = Path.Combine(exchangeRoot, "inventories");
        if (!Directory.Exists(inventoriesRoot))
        {
            return [];
        }

        List<GitPeerContentInventory> inventories = [];
        foreach (string path in Directory.EnumerateFiles(inventoriesRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SignedGitPeerContentInventory? signed = JsonSerializer.Deserialize<SignedGitPeerContentInventory>(
                    await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
                GitPeerContentInventory? inventory = signed?.Inventory;
                if (inventory is null || inventory.ProjectId != projectId ||
                    inventory.Device.DeviceId == localDeviceId ||
                    !TryAuthorize(inventory.ProjectId, projectId, inventory.Device, authorized, out DeviceIdentity? device) ||
                    !PeerExchangeCodec.VerifySignature(device, CanonicalBytes(inventory), signed!.Signature))
                {
                    continue;
                }

                inventories.Add(inventory);
            }
            catch (Exception exception) when (exception is IOException or JsonException or CryptographicException)
            {
                // A partially synchronized or invalid inventory is ignored.
            }
        }

        return inventories;
    }

    private async Task<IReadOnlyList<VerifiedLfsRequest>> ReadVerifiedRequestsAsync(
        Guid projectId,
        string exchangeRoot,
        IReadOnlyDictionary<Guid, DeviceIdentity> authorized,
        CancellationToken cancellationToken)
    {
        string requestsRoot = Path.Combine(exchangeRoot, "requests");
        if (!Directory.Exists(requestsRoot))
        {
            return [];
        }

        List<VerifiedLfsRequest> requests = [];
        foreach (string path in Directory.EnumerateFiles(requestsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SignedPeerLfsObjectRequest? signed = JsonSerializer.Deserialize<SignedPeerLfsObjectRequest>(
                    await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
                PeerLfsObjectRequest? request = signed?.Request;
                if (request is null || request.ProjectId != projectId || request.ExpiresAt <= DateTimeOffset.UtcNow ||
                    !IsSha256(request.OidSha256) ||
                    !TryAuthorize(request.ProjectId, projectId, request.Requester, authorized, out DeviceIdentity? requester) ||
                    !PeerExchangeCodec.VerifySignature(requester, CanonicalBytes(request), signed!.Signature))
                {
                    continue;
                }

                requests.Add(new VerifiedLfsRequest(request, path));
            }
            catch (Exception exception) when (exception is IOException or JsonException or CryptographicException)
            {
                // A partially synchronized or invalid request is ignored.
            }
        }

        return requests;
    }

    private async Task<HashSet<string>> ReadLocalRequestedOidsAsync(
        Guid projectId,
        string exchangeRoot,
        Guid? localDeviceId,
        IReadOnlyDictionary<Guid, DeviceIdentity> authorized,
        CancellationToken cancellationToken)
    {
        if (localDeviceId is null)
        {
            return [];
        }

        IReadOnlyList<VerifiedLfsRequest> requests = await ReadVerifiedRequestsAsync(
            projectId, exchangeRoot, authorized, cancellationToken);
        return requests.Where(request => request.Request.Requester.DeviceId == localDeviceId)
            .Select(request => request.Request.OidSha256)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static PeerLfsAvailabilityCache BuildAvailabilityCache(
        Guid projectId,
        IReadOnlyList<GitPeerContentInventory> inventories)
    {
        Dictionary<string, List<PeerLfsAvailabilityLocation>> locations = new(StringComparer.OrdinalIgnoreCase);
        foreach (GitPeerContentInventory inventory in inventories.OrderByDescending(item => item.CreatedAt))
        {
            foreach (PeerLfsInventoryObject item in inventory.LfsObjects)
            {
                if (!IsSha256(item.OidSha256))
                {
                    continue;
                }

                if (!locations.TryGetValue(item.OidSha256, out List<PeerLfsAvailabilityLocation>? peers))
                {
                    peers = [];
                    locations[item.OidSha256] = peers;
                }

                if (peers.Any(peer => peer.DeviceId == inventory.Device.DeviceId))
                {
                    continue;
                }

                peers.Add(new PeerLfsAvailabilityLocation(
                    inventory.Device.DeviceId,
                    inventory.Device.DisplayName,
                    inventory.CreatedAt,
                    item.Size,
                    item.Priority,
                    item.PublishedToExchange));
            }
        }

        return new PeerLfsAvailabilityCache(
            projectId,
            DateTimeOffset.UtcNow,
            locations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PeerLfsObjectAvailability(pair.Key, pair.Value.ToArray()))
                .ToArray());
    }

    private static async Task<ResumableCopyResult> CopyResumableVerifiedAsync(
        string sourcePath,
        string destinationPath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            if (!string.Equals(await ComputeFileHashAsync(destinationPath, cancellationToken), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Existing LFS object '{expectedHash}' is corrupt.");
            }

            return new ResumableCopyResult(false, false, 0);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string partialPath = destinationPath + ".cyrevision-partial";
        long sourceLength = new FileInfo(sourcePath).Length;
        long offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (offset > sourceLength || (offset > 0 && !await PrefixMatchesAsync(sourcePath, partialPath, cancellationToken)))
        {
            File.Delete(partialPath);
            offset = 0;
        }

        bool resumed = offset > 0;
        await using (FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (FileStream partial = new(partialPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            source.Seek(offset, SeekOrigin.Begin);
            partial.Seek(offset, SeekOrigin.Begin);
            partial.SetLength(offset);
            await source.CopyToAsync(partial, 1024 * 1024, cancellationToken);
            await partial.FlushAsync(cancellationToken);
        }

        if (!string.Equals(await ComputeFileHashAsync(partialPath, cancellationToken), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidDataException($"Transferred LFS object '{expectedHash}' failed SHA-256 verification.");
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(partialPath);
            return new ResumableCopyResult(false, resumed, 0);
        }

        File.Move(partialPath, destinationPath);
        return new ResumableCopyResult(true, resumed, sourceLength - offset);
    }

    private static async Task<bool> PrefixMatchesAsync(
        string sourcePath,
        string partialPath,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
        await using FileStream partial = new(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
        byte[] sourceBuffer = new byte[bufferSize];
        byte[] partialBuffer = new byte[bufferSize];
        while (true)
        {
            int partialRead = await partial.ReadAsync(partialBuffer, cancellationToken);
            if (partialRead == 0)
            {
                return true;
            }

            int sourceRead = await source.ReadAtLeastAsync(sourceBuffer.AsMemory(0, partialRead), partialRead, false, cancellationToken);
            if (sourceRead != partialRead || !sourceBuffer.AsSpan(0, sourceRead).SequenceEqual(partialBuffer.AsSpan(0, partialRead)))
            {
                return false;
            }
        }
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

        try
        {
            Guid[]? values = JsonSerializer.Deserialize<Guid[]>(await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
            return values?.ToHashSet() ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return [];
        }
    }

    private static Task WriteProcessedTransactionsAsync(
        string stateRoot,
        HashSet<Guid> processed,
        CancellationToken cancellationToken) =>
        WriteJsonAtomicallyAsync(
            Path.Combine(stateRoot, "processed-transactions.json"),
            processed.OrderBy(id => id).ToArray(),
            cancellationToken);

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void EnsureIgnoreFile(string exchangeRoot)
    {
        string ignorePath = Path.Combine(exchangeRoot, ".stignore");
        string current = File.Exists(ignorePath) ? File.ReadAllText(ignorePath) : string.Empty;
        string[] rules = ["(?d).cyrevision-staging", "(?d)**/*.cyrevision-partial", "(?d)**/*.tmp"];
        string missing = string.Join(Environment.NewLine, rules.Where(rule => !current.Contains(rule, StringComparison.Ordinal)));
        if (missing.Length > 0)
        {
            File.AppendAllText(ignorePath, (current.Length > 0 && !current.EndsWith('\n') ? Environment.NewLine : string.Empty) + missing + Environment.NewLine);
        }
    }

    private static Dictionary<Guid, DeviceIdentity> CreateAuthorizedDeviceMap(
        IReadOnlyCollection<DeviceIdentity> devices,
        DeviceIdentity? additional = null)
    {
        Dictionary<Guid, DeviceIdentity> result = devices.GroupBy(device => device.DeviceId)
            .ToDictionary(group => group.Key, group => group.First());
        if (additional is not null)
        {
            result[additional.DeviceId] = additional;
        }

        return result;
    }

    private static bool TryAuthorize(
        Guid actualProjectId,
        Guid expectedProjectId,
        DeviceIdentity claimed,
        IReadOnlyDictionary<Guid, DeviceIdentity> authorized,
        out DeviceIdentity device)
    {
        if (actualProjectId == expectedProjectId &&
            authorized.TryGetValue(claimed.DeviceId, out DeviceIdentity? known) &&
            string.Equals(known.SigningPublicKey, claimed.SigningPublicKey, StringComparison.Ordinal))
        {
            device = known;
            return true;
        }

        device = null!;
        return false;
    }

    private static byte[] CanonicalBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static string GetExchangeLfsObjectPath(string exchangeRoot, string oid) =>
        Path.Combine(exchangeRoot, "lfs", "objects", oid[..2], oid.Substring(2, 2), oid);

    private static bool IsPublishedObjectPresent(string exchangeRoot, string oid) =>
        File.Exists(GetExchangeLfsObjectPath(exchangeRoot, oid));

    private static string GetAvailabilityCachePath(string stateRoot) =>
        Path.Combine(stateRoot, "lfs-availability-cache.json");

    private static IEnumerable<(string Oid, string Path)> ParseLfsList(string output)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && IsSha256(fields[0]))
            {
                yield return (fields[0].ToLowerInvariant(), fields.Length == 3 ? fields[2] : string.Empty);
            }
        }
    }

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

    private sealed record LocalLfsObject(
        string OidSha256,
        string LocalObjectPath,
        long Size,
        PeerLfsObjectPriority Priority,
        int HistoricalIndex,
        IReadOnlyList<string> Paths);

    private sealed class MutableLocalLfsObject(
        string oidSha256,
        string localObjectPath,
        long size,
        PeerLfsObjectPriority priority,
        int historicalIndex)
    {
        public string OidSha256 { get; } = oidSha256;
        public string LocalObjectPath { get; } = localObjectPath;
        public long Size { get; } = size;
        public PeerLfsObjectPriority Priority { get; } = priority;
        public int HistoricalIndex { get; } = historicalIndex;
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public LocalLfsObject ToRecord() => new(
            OidSha256,
            LocalObjectPath,
            Size,
            Priority,
            HistoricalIndex,
            Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private sealed record ImportCandidate(
        string OidSha256,
        string SourcePath,
        long Size,
        PeerLfsObjectPriority Priority,
        bool Selected);

    private sealed record VerifiedLfsRequest(PeerLfsObjectRequest Request, string Path);
    private sealed record ResumableCopyResult(bool Copied, bool Resumed, long CopiedBytes);
    private sealed record TransferBatchResult(
        int CopiedObjects,
        int ResumedObjects,
        int DeferredObjects,
        long CopiedBytes,
        IReadOnlyList<string> CompletedOids);
}
