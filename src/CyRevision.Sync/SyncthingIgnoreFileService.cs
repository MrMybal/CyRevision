using System.Text;

namespace CyRevision.Sync;

public sealed class SyncthingIgnoreFileService
{
    public const string FileName = ".stignore";

    public static string UnrealTemplate => string.Join('\n',
        "// CyRevision Unreal Engine defaults",
        "// Edit this list before saving if your project needs one of these folders.",
        "(?d).DS_Store",
        "(?d)Thumbs.db",
        "/.git",
        "/.vs",
        "/.idea",
        "/Binaries",
        "/DerivedDataCache",
        "/Intermediate",
        "/Saved",
        string.Empty);

    public async Task<string> ReadAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(folderPath);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            : string.Empty;
    }

    public async Task WriteAsync(
        string folderPath,
        string contents,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(folderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string normalized = Normalize(contents);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, normalized, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static string Normalize(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd() + Environment.NewLine;

    private static string ResolvePath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("A synchronized folder path is required.", nameof(folderPath));
        }

        return Path.Combine(Path.GetFullPath(folderPath), FileName);
    }
}
