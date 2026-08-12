using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

internal sealed class UnrealBridgeServer : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 64 * 1024;
    private readonly int _port;
    private readonly Func<IReadOnlyCollection<UnrealConnectionRegistration>> _registrations;
    private readonly string _applicationVersion;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptLoop;

    public UnrealBridgeServer(
        int port,
        string applicationVersion,
        Func<IReadOnlyCollection<UnrealConnectionRegistration>> registrations)
    {
        _port = port;
        _applicationVersion = applicationVersion;
        _registrations = registrations;
    }

    public event EventHandler<UnrealProjectChangedEventArgs>? ProjectChanged;

    public string Endpoint => $"http://127.0.0.1:{_port}/cyrevision/v1/";

    public string? Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _cancellation = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_cancellation.Token);
            return null;
        }
        catch (SocketException exception)
        {
            _listener = null;
            return exception.Message;
        }
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
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        }

        _cancellation?.Dispose();
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
            BridgeRequest? request = await ReadRequestAsync(stream, cancellationToken);
            if (request is null)
            {
                await WriteResponseAsync(stream, 400, new { error = "invalid_request" }, cancellationToken);
                return;
            }

            UnrealConnectionRegistration? registration = Authorize(request.BearerToken);
            if (registration is null)
            {
                await WriteResponseAsync(stream, 401, new { error = "unauthorized" }, cancellationToken);
                return;
            }

            string route = request.Path.Split('?', 2)[0].TrimEnd('/');
            if (request.Method == "GET" && route.Equals("/cyrevision/v1/status", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, 200, new
                {
                    application = "CyRevision",
                    version = _applicationVersion,
                    plugin = "0.1.0",
                    projectRoot = registration.ProjectRoot,
                    capabilities = new[]
                    {
                        "git", "git-lfs", "sync", "backup", "asset-diff", "advisory-reservations",
                        "swarm-over-vpn", "vpn-file-exchange"
                    }
                }, cancellationToken);
                return;
            }

            if (request.Method == "POST" && route.Equals("/cyrevision/v1/notify", StringComparison.OrdinalIgnoreCase))
            {
                ProjectChanged?.Invoke(this, new UnrealProjectChangedEventArgs(
                    registration.ProjectRoot,
                    ReadAction(request.Body)));
                await WriteResponseAsync(stream, 202, new { accepted = true }, cancellationToken);
                return;
            }

            await WriteResponseAsync(stream, 404, new { error = "not_found" }, cancellationToken);
        }
    }

    private UnrealConnectionRegistration? Authorize(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        foreach (UnrealConnectionRegistration registration in _registrations())
        {
            byte[] expected = Encoding.UTF8.GetBytes(registration.Token);
            byte[] supplied = Encoding.UTF8.GetBytes(token);
            if (expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                return registration;
            }
        }
        return null;
    }

    private static async Task<BridgeRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] headerBuffer = new byte[MaxHeaderBytes];
        int total = 0;
        int headerEnd = -1;
        while (total < headerBuffer.Length)
        {
            int read = await stream.ReadAsync(headerBuffer.AsMemory(total, headerBuffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            headerEnd = FindHeaderEnd(headerBuffer, total);
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0)
        {
            return null;
        }

        string[] lines = Encoding.ASCII.GetString(headerBuffer, 0, headerEnd)
            .Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            return null;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        int contentLength = headers.TryGetValue("Content-Length", out string? value) && int.TryParse(value, out int parsed)
            ? Math.Clamp(parsed, 0, MaxBodyBytes)
            : 0;
        int bodyStart = headerEnd + 4;
        using MemoryStream body = new();
        if (total > bodyStart)
        {
            body.Write(headerBuffer, bodyStart, Math.Min(total - bodyStart, contentLength));
        }
        while (body.Length < contentLength)
        {
            byte[] chunk = new byte[Math.Min(8192, contentLength - (int)body.Length)];
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            body.Write(chunk, 0, read);
        }

        string? bearer = headers.TryGetValue("Authorization", out string? authorization) &&
                         authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
        return new BridgeRequest(
            requestLine[0].ToUpperInvariant(),
            requestLine[1],
            bearer,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (int index = 3; index < length; index++)
        {
            if (buffer[index - 3] == '\r' && buffer[index - 2] == '\n' &&
                buffer[index - 1] == '\r' && buffer[index] == '\n')
            {
                return index - 3;
            }
        }
        return -1;
    }

    private static string ReadAction(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("action", out JsonElement action)
                ? action.GetString() ?? "unreal-change"
                : "unreal-change";
        }
        catch (JsonException)
        {
            return "unreal-change";
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        object payload,
        CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload);
        string reason = status switch
        {
            200 => "OK",
            202 => "Accepted",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            _ => "Error"
        };
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private sealed record BridgeRequest(string Method, string Path, string? BearerToken, string Body);
}

internal sealed record UnrealConnectionRegistration(string ProjectRoot, string Token, string ExecutablePath);
