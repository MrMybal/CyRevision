using System.Security.Cryptography;

namespace CyRevision.Discord.Agent;

public static class ControlTokenProvider
{
    public static async Task<(string Token, bool Created)> LoadOrCreateAsync(
        string path,
        CancellationToken cancellationToken = default,
        bool useEnvironmentOverride = true)
    {
        string? environmentToken = useEnvironmentOverride
            ? Environment.GetEnvironmentVariable("CYREVISION_DISCORD_AGENT_TOKEN")
            : null;
        if (!string.IsNullOrWhiteSpace(environmentToken))
        {
            Validate(environmentToken);
            return (environmentToken.Trim(), false);
        }

        if (File.Exists(path))
        {
            string existing = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            Validate(existing);
            return (existing, false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string temporaryPath = path + ".new";
        await File.WriteAllTextAsync(temporaryPath, token, cancellationToken);
        Restrict(temporaryPath);
        File.Move(temporaryPath, path, overwrite: false);
        Restrict(path);
        return (token, true);
    }

    public static bool IsValid(string expectedToken, string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suppliedToken = authorizationHeader[prefix.Length..].Trim();
        byte[] expectedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expectedToken));
        byte[] suppliedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static void Validate(string token)
    {
        if (token.Trim().Length < 32)
        {
            throw new InvalidDataException("The autonomous agent control token must contain at least 32 characters.");
        }
    }

    private static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
