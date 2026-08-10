using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Core.Updates;

public enum UpdatePlatform
{
    Windows,
    Linux,
    MacOs,
    Unsupported
}

public sealed record ReleaseAsset(string Name, Uri DownloadUri, long Size);

public sealed record ApplicationUpdateInfo(
    ReleaseVersion CurrentVersion,
    ReleaseVersion LatestVersion,
    Uri ReleasePage,
    DateTimeOffset? PublishedAt,
    string ReleaseNotes,
    ReleaseAsset? Package,
    ReleaseAsset? Checksums)
{
    public bool IsUpdateAvailable => LatestVersion.CompareTo(CurrentVersion) > 0;
}

public readonly record struct ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<ReleaseVersion>
{
    public int CompareTo(ReleaseVersion other)
    {
        int coreComparison = Major.CompareTo(other.Major);
        if (coreComparison == 0)
        {
            coreComparison = Minor.CompareTo(other.Minor);
        }

        if (coreComparison == 0)
        {
            coreComparison = Patch.CompareTo(other.Patch);
        }

        if (coreComparison != 0)
        {
            return coreComparison;
        }

        bool thisIsStable = string.IsNullOrWhiteSpace(PreRelease);
        bool otherIsStable = string.IsNullOrWhiteSpace(other.PreRelease);
        if (thisIsStable || otherIsStable)
        {
            return thisIsStable.CompareTo(otherIsStable);
        }

        string[] leftParts = PreRelease!.Split('.');
        string[] rightParts = other.PreRelease!.Split('.');
        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            string left = leftParts[index];
            string right = rightParts[index];
            bool leftIsNumber = int.TryParse(left, out int leftNumber);
            bool rightIsNumber = int.TryParse(right, out int rightNumber);
            int comparison = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : leftIsNumber != rightIsNumber
                    ? (leftIsNumber ? -1 : 1)
                    : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(PreRelease)
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public static ReleaseVersion Parse(string value)
    {
        if (!TryParse(value, out ReleaseVersion version))
        {
            throw new FormatException($"Invalid release version: {value}");
        }

        return version;
    }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().TrimStart('v', 'V');
        int buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0)
        {
            normalized = normalized[..buildSeparator];
        }

        string? preRelease = null;
        int preReleaseSeparator = normalized.IndexOf('-');
        if (preReleaseSeparator >= 0)
        {
            preRelease = normalized[(preReleaseSeparator + 1)..];
            normalized = normalized[..preReleaseSeparator];
            if (string.IsNullOrWhiteSpace(preRelease))
            {
                return false;
            }
        }

        string[] parts = normalized.Split('.');
        if (parts.Length < 2 || parts.Length > 3 ||
            !int.TryParse(parts[0], out int major) || major < 0 ||
            !int.TryParse(parts[1], out int minor) || minor < 0 ||
            (parts.Length > 2 && (!int.TryParse(parts[2], out _) || int.Parse(parts[2]) < 0)))
        {
            return false;
        }

        int patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        version = new ReleaseVersion(major, minor, patch, preRelease);
        return true;
    }
}

public sealed class ApplicationUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _latestReleaseApi;

    public ApplicationUpdateService(Uri repositoryUri, ReleaseVersion currentVersion, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryUri);
        if (!repositoryUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !repositoryUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The update repository must be an HTTPS GitHub repository.", nameof(repositoryUri));
        }

        string[] segments = repositoryUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new ArgumentException("The update repository URL must contain an owner and repository name.", nameof(repositoryUri));
        }

        CurrentVersion = currentVersion;
        RepositoryUri = new Uri($"https://github.com/{segments[0]}/{segments[1]}");
        _latestReleaseApi = new Uri($"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest");
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _ownsHttpClient = httpClient is null;
    }

    public Uri RepositoryUri { get; }

    public ReleaseVersion CurrentVersion { get; }

    public async Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(_latestReleaseApi);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;

        bool isDraft = root.TryGetProperty("draft", out JsonElement draftElement) && draftElement.GetBoolean();
        bool isPreRelease = root.TryGetProperty("prerelease", out JsonElement preReleaseElement) && preReleaseElement.GetBoolean();
        if (isDraft || isPreRelease)
        {
            throw new InvalidDataException("The update endpoint returned a draft or prerelease instead of a stable release.");
        }

        string tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The release has no tag.");
        ReleaseVersion latestVersion = ReleaseVersion.Parse(tag);
        Uri releasePage = ReadHttpsUri(root.GetProperty("html_url").GetString(), "release page");
        string notes = root.TryGetProperty("body", out JsonElement notesElement)
            ? notesElement.GetString() ?? string.Empty
            : string.Empty;
        DateTimeOffset? publishedAt = root.TryGetProperty("published_at", out JsonElement dateElement) &&
                                      DateTimeOffset.TryParse(dateElement.GetString(), out DateTimeOffset parsedDate)
            ? parsedDate
            : null;

        List<ReleaseAsset> assets = [];
        if (root.TryGetProperty("assets", out JsonElement assetsElement))
        {
            foreach (JsonElement assetElement in assetsElement.EnumerateArray())
            {
                string? name = assetElement.GetProperty("name").GetString();
                string? downloadUrl = assetElement.GetProperty("browser_download_url").GetString();
                long size = assetElement.TryGetProperty("size", out JsonElement sizeElement)
                    ? sizeElement.GetInt64()
                    : 0;
                if (!string.IsNullOrWhiteSpace(name) && Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) &&
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(new ReleaseAsset(name, uri, size));
                }
            }
        }

        (UpdatePlatform platform, Architecture architecture) = DetectCurrentPlatform();
        ReleaseAsset? package = SelectPackage(assets, platform, architecture);
        ReleaseAsset? checksums = assets.FirstOrDefault(asset =>
            asset.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        return new ApplicationUpdateInfo(
            CurrentVersion,
            latestVersion,
            releasePage,
            publishedAt,
            notes,
            package,
            checksums);
    }

    public async Task<string> DownloadAsync(
        ApplicationUpdateInfo update,
        string updatesDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesDirectory);
        ReleaseAsset package = update.Package ?? throw new InvalidOperationException("No compatible update package is available.");
        ReleaseAsset checksums = update.Checksums ?? throw new InvalidDataException("The release does not contain SHA256SUMS.txt.");

        string packageName = Path.GetFileName(package.Name);
        if (string.IsNullOrWhiteSpace(packageName) || !packageName.Equals(package.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update package name is unsafe.");
        }

        string checksumManifest = await DownloadTextAsync(checksums.DownloadUri, cancellationToken);
        string expectedHash = ReadExpectedHash(checksumManifest, packageName) ??
                              throw new InvalidDataException($"No SHA-256 checksum was published for {packageName}.");

        string versionDirectory = Path.Combine(updatesDirectory, update.LatestVersion.ToString());
        Directory.CreateDirectory(versionDirectory);
        string destinationPath = Path.Combine(versionDirectory, packageName);
        string temporaryPath = destinationPath + ".download";

        try
        {
            await DownloadFileAsync(package, temporaryPath, progress, cancellationToken);
            string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(1);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public static ReleaseVersion ReadCurrentVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (ReleaseVersion.TryParse(informationalVersion, out ReleaseVersion version))
        {
            return version;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        return new ReleaseVersion(
            Math.Max(0, assemblyVersion?.Major ?? 0),
            Math.Max(0, assemblyVersion?.Minor ?? 0),
            Math.Max(0, assemblyVersion?.Build ?? 0));
    }

    public static (UpdatePlatform Platform, Architecture Architecture) DetectCurrentPlatform()
    {
        UpdatePlatform platform = OperatingSystem.IsWindows()
            ? UpdatePlatform.Windows
            : OperatingSystem.IsLinux()
                ? UpdatePlatform.Linux
                : OperatingSystem.IsMacOS()
                    ? UpdatePlatform.MacOs
                    : UpdatePlatform.Unsupported;
        return (platform, RuntimeInformation.ProcessArchitecture);
    }

    public static ReleaseAsset? SelectPackage(
        IEnumerable<ReleaseAsset> assets,
        UpdatePlatform platform,
        Architecture architecture)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string? runtime = GetRuntimeIdentifier(platform, architecture);
        if (runtime is null)
        {
            return null;
        }

        ReleaseAsset[] compatibleAssets = assets
            .Where(asset => asset.Name.Contains(runtime, StringComparison.OrdinalIgnoreCase))
            .Where(asset => !asset.Name.Contains("SHA256SUMS", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return platform switch
        {
            UpdatePlatform.Windows => compatibleAssets
                .OrderByDescending(asset => asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(),
            UpdatePlatform.Linux => compatibleAssets
                .OrderByDescending(asset => asset.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(asset => asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(),
            UpdatePlatform.MacOs => compatibleAssets
                .OrderByDescending(asset => asset.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(),
            _ => null
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task DownloadFileAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(asset.DownloadUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalLength = response.Content.Headers.ContentLength ?? asset.Size;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        byte[] buffer = new byte[81920];
        long downloaded = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            if (totalLength > 0)
            {
                progress?.Report(Math.Clamp(downloaded / (double)totalLength, 0, 1));
            }
        }
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(uri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CyRevision", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static Uri ReadHttpsUri(string? value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The {description} URL is invalid.");
        }

        return uri;
    }

    private static string? GetRuntimeIdentifier(UpdatePlatform platform, Architecture architecture)
    {
        string? architectureName = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (architectureName is null)
        {
            return null;
        }

        return platform switch
        {
            UpdatePlatform.Windows when architecture == Architecture.Arm64 => "win-x64",
            UpdatePlatform.Windows => $"win-{architectureName}",
            UpdatePlatform.Linux => $"linux-{architectureName}",
            UpdatePlatform.MacOs => $"osx-{architectureName}",
            _ => null
        };
    }

    private static string? ReadExpectedHash(string manifest, string packageName)
    {
        foreach (string line in manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 67)
            {
                continue;
            }

            string hash = trimmed[..64];
            string name = trimmed[64..].TrimStart(' ', '*');
            if (name.Equals(packageName, StringComparison.Ordinal) &&
                hash.All(character => char.IsAsciiHexDigit(character)))
            {
                return hash;
            }
        }

        return null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
