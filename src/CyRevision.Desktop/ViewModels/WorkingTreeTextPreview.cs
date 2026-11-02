using System.Text;

namespace CyRevision.Desktop.ViewModels;

/// <summary>A bounded text preview, never a reason to read an entire large build output.</summary>
internal static class WorkingTreeTextPreview
{
    internal const int MaximumInputBytes = 8 * 1024 * 1024;
    internal const int MaximumCharacters = 1024 * 1024;
    internal const int MaximumLines = 20_000;

    internal static async Task<string> BuildDiffAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(repositoryRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            return "The untracked file is no longer available.";
        // Do not follow untracked links outside the repository while preparing a preview.
        for (FileSystemInfo? item = new FileInfo(path); item is not null && item.FullName != root; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if (item.LinkTarget is not null) return "Symbolic link — open the target explicitly to preview its contents.";

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] bytes = new byte[(int)Math.Min(stream.Length, MaximumInputBytes)];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        bool truncated = stream.Length > count;
        Encoding encoding = Encoding.UTF8;
        int offset = 0;
        if (count >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = Encoding.UTF32; offset = 4; }
        else if (count >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false); offset = 4; }
        else if (count >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { encoding = Encoding.Unicode; offset = 2; }
        else if (count >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { encoding = Encoding.BigEndianUnicode; offset = 2; }
        else if (count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) offset = 3;
        else if (bytes.AsSpan(0, count).Contains((byte)0)) return "Untracked binary file — no textual diff available.";

        string text = encoding.GetString(bytes, offset, count - offset).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.Contains('\0')) return "Untracked binary file — no textual diff available.";
        List<string> lines = [];
        int characters = 0;
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lines.Count == MaximumLines || characters >= MaximumCharacters) { truncated = true; break; }
            int remaining = MaximumCharacters - characters;
            if (line.Length > remaining)
            {
                // Avoid cutting a UTF-16 surrogate pair before JSON transport encodes it.
                if (remaining > 0 && char.IsHighSurrogate(line[remaining - 1])) remaining--;
                lines.Add(line[..remaining]); truncated = true; break;
            }
            lines.Add(line); characters += line.Length + 1;
        }
        StringBuilder diff = new();
        diff.AppendLine($"diff --git a/{relativePath} b/{relativePath}");
        diff.AppendLine("new file mode 100644");
        diff.AppendLine("--- /dev/null");
        diff.AppendLine($"+++ b/{relativePath}");
        diff.AppendLine($"@@ -0,0 +1,{lines.Count} @@");
        foreach (string line in lines) diff.Append('+').AppendLine(line);
        if (truncated) diff.AppendLine("[Preview truncated: showing the beginning of this file within the text/line limit. The file on disk is unchanged.]");
        return diff.ToString();
    }
}
