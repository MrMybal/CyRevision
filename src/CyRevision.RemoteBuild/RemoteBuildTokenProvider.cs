using System.Security.Cryptography;

namespace CyRevision.RemoteBuild;

public static class RemoteBuildTokenProvider
{
    public static async Task<(string Token, bool Created)> LoadOrCreateAsync(
        string tokenPath,
        CancellationToken cancellationToken = default)
    {
        string? environment = Environment.GetEnvironmentVariable("CYREVISION_BUILD_AGENT_TOKEN");
        if (!string.IsNullOrWhiteSpace(environment))
            return (environment.Trim(), false);
        string path = Path.GetFullPath(tokenPath);
        if (File.Exists(path))
            return ((await File.ReadAllTextAsync(path, cancellationToken)).Trim(), false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await File.WriteAllTextAsync(path, token, cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return (token, true);
    }

    public static bool IsValid(string expected, string authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] actualBytes = System.Text.Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..].Trim());
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
