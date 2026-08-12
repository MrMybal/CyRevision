using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class VpnFileExchangeService
{
    private const int ProtocolVersion = 1;
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxListedFiles = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public VpnFileExchangeHost CreateHost(
        VpnFileExchangeCredentials credentials,
        VpnProjectProfile vpnProfile)
    {
        ValidateProfile(credentials.Profile, credentials.AccessToken);
        VpnProfileValidator.Validate(vpnProfile);
        if (credentials.Profile.ProjectId != vpnProfile.ProjectId)
        {
            throw new InvalidDataException("The file-exchange profile belongs to another project.");
        }
        return new VpnFileExchangeHost(credentials, vpnProfile.NetworkCidr);
    }

    public async Task<string> TestAsync(
        string peerAddress,
        int port,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using TcpClient client = await ConnectAsync(peerAddress, port, cancellationToken);
        NetworkStream stream = client.GetStream();
        await WriteHeaderAsync(stream, new TransferHeader(ProtocolVersion, "ping", accessToken), cancellationToken);
        TransferResponse response = await ReadResponseAsync(stream, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "The peer rejected the connection test.");
        }
        return response.Message ?? "Authenticated VPN file-exchange peer is ready.";
    }

    public async Task<IReadOnlyList<VpnSharedFile>> ListAsync(
        string peerAddress,
        int port,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using TcpClient client = await ConnectAsync(peerAddress, port, cancellationToken);
        NetworkStream stream = client.GetStream();
        await WriteHeaderAsync(stream, new TransferHeader(ProtocolVersion, "list", accessToken), cancellationToken);
        TransferResponse response = await ReadResponseAsync(stream, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "The peer refused folder browsing.");
        }
        return response.Files ?? [];
    }

    public async Task<VpnFileTransferResult> SendFileAsync(
        string peerAddress,
        int port,
        string accessToken,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        FileInfo source = new(Path.GetFullPath(sourcePath));
        if (!source.Exists)
        {
            throw new FileNotFoundException("The file to send does not exist.", source.FullName);
        }
        string hash = await ComputeSha256Async(source.FullName, cancellationToken);
        using TcpClient client = await ConnectAsync(peerAddress, port, cancellationToken);
        NetworkStream stream = client.GetStream();
        await WriteHeaderAsync(stream, new TransferHeader(
            ProtocolVersion, "upload", accessToken, source.Name, source.Length, hash), cancellationToken);
        await using (FileStream input = new(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await input.CopyToAsync(stream, 128 * 1024, cancellationToken);
        }
        TransferResponse response = await ReadResponseAsync(stream, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "The peer rejected the file.");
        }
        return new VpnFileTransferResult(source.Name, source.Length, hash, response.DestinationPath ?? source.Name);
    }

    public async Task<VpnFileTransferResult> DownloadFileAsync(
        string peerAddress,
        int port,
        string accessToken,
        string remoteRelativePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        using TcpClient client = await ConnectAsync(peerAddress, port, cancellationToken);
        NetworkStream stream = client.GetStream();
        await WriteHeaderAsync(stream, new TransferHeader(
            ProtocolVersion, "download", accessToken, remoteRelativePath), cancellationToken);
        TransferResponse response = await ReadResponseAsync(stream, cancellationToken);
        if (!response.Success || response.Length is null || string.IsNullOrWhiteSpace(response.Sha256))
        {
            throw new InvalidOperationException(response.Error ?? "The peer refused the download.");
        }

        string destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".cyrevision-" + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            string hash = await ReceiveFileAsync(stream, temporary, response.Length.Value, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(response.Sha256)))
            {
                throw new InvalidDataException("The downloaded file failed SHA-256 verification.");
            }
            if (File.Exists(destination))
            {
                throw new IOException("The selected destination already exists; CyRevision never overwrites received files.");
            }
            File.Move(temporary, destination);
            return new VpnFileTransferResult(Path.GetFileName(destination), response.Length.Value, hash, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void ValidateProfile(VpnFileExchangeProfile profile, string accessToken)
    {
        if (profile.ProjectId == Guid.Empty)
        {
            throw new InvalidDataException("The VPN file-exchange profile does not reference a project.");
        }
        if (!IPAddress.TryParse(profile.ListenAddress, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
        {
            throw new InvalidDataException("File exchange must listen on one explicit, non-loopback VPN IPv4 address.");
        }
        if (profile.Port is < 1024 or > 65535)
        {
            throw new InvalidDataException("The VPN file-exchange port must be between 1024 and 65535.");
        }
        if (profile.MaxFileBytes is < 1 or > 100L * 1024 * 1024 * 1024)
        {
            throw new InvalidDataException("The maximum accepted file size must be between 1 byte and 100 GB.");
        }
        if (string.IsNullOrWhiteSpace(profile.InboxPath))
        {
            throw new InvalidDataException("A private inbox folder is required.");
        }
        if ((profile.AllowBrowse || profile.AllowDownload) && string.IsNullOrWhiteSpace(profile.SharedFolderPath))
        {
            throw new InvalidDataException("A shared folder is required when browsing or downloads are enabled.");
        }
        if (Encoding.UTF8.GetByteCount(accessToken) < 32 || accessToken.ContainsAny('\r', '\n'))
        {
            throw new InvalidDataException("The VPN file-exchange access token is invalid.");
        }
    }

    public static string ResolveSharedPath(string root, string relativePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\0'))
        {
            throw new InvalidDataException("The requested shared path is invalid.");
        }
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException("The requested path leaves the configured shared folder.");
        }

        string current = normalizedRoot;
        foreach (string segment in Path.GetRelativePath(normalizedRoot, candidate)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("Symbolic links and junctions are not exposed by VPN folder sharing.");
                }
            }
        }
        return candidate;
    }

    private static async Task<TcpClient> ConnectAsync(
        string peerAddress,
        int port,
        CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(peerAddress, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException("Select a valid IPv4 VPN peer address.");
        }
        TcpClient client = new(AddressFamily.InterNetwork);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static async Task WriteHeaderAsync(NetworkStream stream, object header, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        if (body.Length > MaxHeaderBytes)
        {
            throw new InvalidDataException("The VPN file-exchange header is too large.");
        }
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    internal static async Task<T> ReadHeaderAsync<T>(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken);
        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is < 2 or > MaxHeaderBytes)
        {
            throw new InvalidDataException("The VPN file-exchange header length is invalid.");
        }
        byte[] body = new byte[count];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidDataException("The VPN file-exchange header is invalid.");
    }

    private static Task<TransferResponse> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken) =>
        ReadHeaderAsync<TransferResponse>(stream, cancellationToken);

    internal static async Task<string> ReceiveFileAsync(
        NetworkStream stream,
        string path,
        long length,
        CancellationToken cancellationToken)
    {
        await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The VPN file transfer ended before the announced size was received.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hasher.AppendData(buffer, 0, read);
            remaining -= read;
        }
        await output.FlushAsync(cancellationToken);
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    internal sealed record TransferHeader(
        int Version,
        string Command,
        string Token,
        string? Path = null,
        long? Length = null,
        string? Sha256 = null);

    internal sealed record TransferResponse(
        bool Success,
        string? Error = null,
        string? Message = null,
        long? Length = null,
        string? Sha256 = null,
        string? DestinationPath = null,
        IReadOnlyList<VpnSharedFile>? Files = null);

    internal static int ListedFilesLimit => MaxListedFiles;
}

public sealed class VpnFileExchangeHost : IAsyncDisposable
{
    private readonly VpnFileExchangeCredentials _credentials;
    private readonly uint _network;
    private readonly uint _mask;
    private readonly IPAddress _listenAddress;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptLoop;

    internal VpnFileExchangeHost(VpnFileExchangeCredentials credentials, string networkCidr)
    {
        _credentials = credentials;
        (_network, int prefix) = VpnProfileValidator.ParseCidr(networkCidr);
        _mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        _listenAddress = IPAddress.Parse(credentials.Profile.ListenAddress);
    }

    public bool IsRunning => _listener is not null;

    public string Endpoint => $"{_credentials.Profile.ListenAddress}:{_credentials.Profile.Port}";

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }
        _listener = new TcpListener(_listenAddress, _credentials.Profile.Port);
        _listener.Start();
        _cancellation = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
        }
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException)
            {
            }
        }
        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
        _acceptLoop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsVpnAddress(remote.Address))
                {
                    await SendErrorAsync(stream, "Connections outside the configured VPN subnet are refused.", cancellationToken);
                    return;
                }
                VpnFileExchangeService.TransferHeader request =
                    await VpnFileExchangeService.ReadHeaderAsync<VpnFileExchangeService.TransferHeader>(stream, cancellationToken);
                if (request.Version != 1 || !TokenMatches(request.Token))
                {
                    await SendErrorAsync(stream, "Authentication failed.", cancellationToken);
                    return;
                }

                switch (request.Command.ToLowerInvariant())
                {
                    case "ping":
                        await VpnFileExchangeService.WriteHeaderAsync(stream,
                            new VpnFileExchangeService.TransferResponse(true,
                                Message: $"VPN-only endpoint {Endpoint} is authenticated and ready."), cancellationToken);
                        break;
                    case "list":
                        await HandleListAsync(stream, cancellationToken);
                        break;
                    case "upload":
                        await HandleUploadAsync(stream, request, cancellationToken);
                        break;
                    case "download":
                        await HandleDownloadAsync(stream, request, cancellationToken);
                        break;
                    default:
                        await SendErrorAsync(stream, "Unsupported VPN file-exchange command.", cancellationToken);
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                try
                {
                    await SendErrorAsync(stream, exception.Message, cancellationToken);
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleListAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        VpnFileExchangeProfile profile = _credentials.Profile;
        if (!profile.AllowBrowse)
        {
            await SendErrorAsync(stream, "Folder browsing is disabled on this peer.", cancellationToken);
            return;
        }
        Directory.CreateDirectory(profile.SharedFolderPath);
        List<VpnSharedFile> files = [];
        foreach (string path in Directory.EnumerateFiles(profile.SharedFolderPath, "*", SearchOption.AllDirectories))
        {
            FileInfo file = new(path);
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }
            try
            {
                VpnFileExchangeService.ResolveSharedPath(profile.SharedFolderPath,
                    Path.GetRelativePath(profile.SharedFolderPath, file.FullName));
            }
            catch (InvalidDataException)
            {
                continue;
            }
            files.Add(new VpnSharedFile(
                Path.GetRelativePath(profile.SharedFolderPath, file.FullName).Replace('\\', '/'),
                file.Length,
                file.LastWriteTimeUtc));
            if (files.Count >= VpnFileExchangeService.ListedFilesLimit)
            {
                break;
            }
        }
        await VpnFileExchangeService.WriteHeaderAsync(stream,
            new VpnFileExchangeService.TransferResponse(true, Files: files), cancellationToken);
    }

    private async Task HandleUploadAsync(
        NetworkStream stream,
        VpnFileExchangeService.TransferHeader request,
        CancellationToken cancellationToken)
    {
        VpnFileExchangeProfile profile = _credentials.Profile;
        if (!profile.AllowReceive || request.Length is null || request.Length < 0 ||
            request.Length > profile.MaxFileBytes || string.IsNullOrWhiteSpace(request.Sha256))
        {
            await SendErrorAsync(stream, "Incoming files are disabled or the announced file is invalid/too large.", cancellationToken);
            return;
        }
        string name = Path.GetFileName(request.Path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || name != request.Path || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await SendErrorAsync(stream, "The incoming file name is invalid.", cancellationToken);
            return;
        }
        Directory.CreateDirectory(profile.InboxPath);
        string destination = GetUniqueDestination(profile.InboxPath, name);
        string temporary = destination + ".cyrevision-" + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            string hash = await VpnFileExchangeService.ReceiveFileAsync(stream, temporary, request.Length.Value, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(request.Sha256.ToLowerInvariant())))
            {
                throw new InvalidDataException("The received file failed SHA-256 verification.");
            }
            File.Move(temporary, destination);
            await VpnFileExchangeService.WriteHeaderAsync(stream,
                new VpnFileExchangeService.TransferResponse(true,
                    Length: request.Length, Sha256: hash, DestinationPath: destination), cancellationToken);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task HandleDownloadAsync(
        NetworkStream stream,
        VpnFileExchangeService.TransferHeader request,
        CancellationToken cancellationToken)
    {
        VpnFileExchangeProfile profile = _credentials.Profile;
        if (!profile.AllowDownload || string.IsNullOrWhiteSpace(request.Path))
        {
            await SendErrorAsync(stream, "Downloads are disabled or no shared file was selected.", cancellationToken);
            return;
        }
        string path = VpnFileExchangeService.ResolveSharedPath(profile.SharedFolderPath, request.Path);
        FileInfo file = new(path);
        if (!file.Exists || file.Length > profile.MaxFileBytes)
        {
            await SendErrorAsync(stream, "The shared file does not exist or exceeds the configured size limit.", cancellationToken);
            return;
        }
        string hash;
        await using (FileStream hashing = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(hashing, cancellationToken)).ToLowerInvariant();
        }
        await VpnFileExchangeService.WriteHeaderAsync(stream,
            new VpnFileExchangeService.TransferResponse(true, Length: file.Length, Sha256: hash), cancellationToken);
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await input.CopyToAsync(stream, 128 * 1024, cancellationToken);
    }

    private bool TokenMatches(string supplied)
    {
        byte[] expected = Encoding.UTF8.GetBytes(_credentials.AccessToken);
        byte[] actual = Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private bool IsVpnAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        uint value = VpnProfileValidator.ToUInt32(address);
        return (value & _mask) == _network;
    }

    private static string GetUniqueDestination(string folder, string name)
    {
        string candidate = Path.Combine(folder, name);
        if (!File.Exists(candidate))
        {
            return candidate;
        }
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int index = 1; index < 10_000; index++)
        {
            candidate = Path.Combine(folder, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("No available non-overwriting destination name was found.");
    }

    private static Task SendErrorAsync(NetworkStream stream, string error, CancellationToken cancellationToken) =>
        VpnFileExchangeService.WriteHeaderAsync(stream,
            new VpnFileExchangeService.TransferResponse(false, Error: error), cancellationToken);
}

file static class VpnFileExchangeStringExtensions
{
    public static bool ContainsAny(this string value, params char[] characters) => characters.Any(value.Contains);
}
