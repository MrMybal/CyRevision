using System.Text.RegularExpressions;

namespace CyRevision.Diff;

public enum CodeDiffLineKind
{
    Context,
    Added,
    Removed,
    HunkHeader,
    FileHeader,
    Metadata
}

public sealed record CodeDiffLine(
    int Index,
    CodeDiffLineKind Kind,
    string RawText,
    string Content,
    int? OldLineNumber,
    int? NewLineNumber)
{
    public bool IsChange => Kind is CodeDiffLineKind.Added or CodeDiffLineKind.Removed;
}

public sealed record CodeDiffSplitRow(
    CodeDiffLine? Left,
    CodeDiffLine? Right,
    CodeDiffLine? FullWidth);

public sealed record ParsedCodeDiff(
    IReadOnlyList<CodeDiffLine> Lines,
    IReadOnlyList<CodeDiffSplitRow> SplitRows,
    int AddedLineCount,
    int RemovedLineCount,
    int HunkCount);

public sealed partial class CodeDiffParser
{
    public ParsedCodeDiff Parse(string? diffText)
    {
        if (string.IsNullOrEmpty(diffText))
        {
            return new ParsedCodeDiff([], [], 0, 0, 0);
        }

        string normalized = diffText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] rawLines = normalized.Split('\n');
        if (rawLines.Length > 0 && rawLines[^1].Length == 0)
        {
            rawLines = rawLines[..^1];
        }

        List<CodeDiffLine> lines = new(rawLines.Length);
        int? oldLineNumber = null;
        int? newLineNumber = null;
        int addedLineCount = 0;
        int removedLineCount = 0;
        int hunkCount = 0;

        for (int index = 0; index < rawLines.Length; index++)
        {
            string rawLine = rawLines[index];
            CodeDiffLine line;

            Match hunkMatch = HunkHeaderRegex().Match(rawLine);
            if (hunkMatch.Success)
            {
                oldLineNumber = int.Parse(hunkMatch.Groups["old"].Value);
                newLineNumber = int.Parse(hunkMatch.Groups["new"].Value);
                hunkCount++;
                line = new CodeDiffLine(index, CodeDiffLineKind.HunkHeader, rawLine, rawLine, null, null);
            }
            else if (IsFileHeader(rawLine))
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.FileHeader, rawLine, rawLine, null, null);
            }
            else if (rawLine.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.Metadata, rawLine, rawLine, null, null);
            }
            else if (oldLineNumber.HasValue && newLineNumber.HasValue && rawLine.StartsWith('+'))
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.Added, rawLine, StripPrefix(rawLine), null, newLineNumber);
                newLineNumber++;
                addedLineCount++;
            }
            else if (oldLineNumber.HasValue && newLineNumber.HasValue && rawLine.StartsWith('-'))
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.Removed, rawLine, StripPrefix(rawLine), oldLineNumber, null);
                oldLineNumber++;
                removedLineCount++;
            }
            else if (oldLineNumber.HasValue && newLineNumber.HasValue && rawLine.StartsWith(' '))
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.Context, rawLine, StripPrefix(rawLine), oldLineNumber, newLineNumber);
                oldLineNumber++;
                newLineNumber++;
            }
            else
            {
                line = new CodeDiffLine(index, CodeDiffLineKind.Metadata, rawLine, rawLine, null, null);
            }

            lines.Add(line);
        }

        return new ParsedCodeDiff(
            lines,
            BuildSplitRows(lines),
            addedLineCount,
            removedLineCount,
            hunkCount);
    }

    private static IReadOnlyList<CodeDiffSplitRow> BuildSplitRows(IReadOnlyList<CodeDiffLine> lines)
    {
        List<CodeDiffSplitRow> rows = [];
        int index = 0;
        while (index < lines.Count)
        {
            CodeDiffLine line = lines[index];
            if (line.Kind is not (CodeDiffLineKind.Added or CodeDiffLineKind.Removed or CodeDiffLineKind.Context))
            {
                rows.Add(new CodeDiffSplitRow(null, null, line));
                index++;
                continue;
            }

            if (line.Kind == CodeDiffLineKind.Context)
            {
                rows.Add(new CodeDiffSplitRow(line, line, null));
                index++;
                continue;
            }

            List<CodeDiffLine> removed = [];
            while (index < lines.Count && lines[index].Kind == CodeDiffLineKind.Removed)
            {
                removed.Add(lines[index++]);
            }

            List<CodeDiffLine> added = [];
            while (index < lines.Count && lines[index].Kind == CodeDiffLineKind.Added)
            {
                added.Add(lines[index++]);
            }

            if (removed.Count == 0 && added.Count == 0)
            {
                // A block can start with additions. Consume it here instead of waiting for the next iteration.
                while (index < lines.Count && lines[index].Kind == CodeDiffLineKind.Added)
                {
                    added.Add(lines[index++]);
                }
            }

            int rowCount = Math.Max(removed.Count, added.Count);
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                rows.Add(new CodeDiffSplitRow(
                    rowIndex < removed.Count ? removed[rowIndex] : null,
                    rowIndex < added.Count ? added[rowIndex] : null,
                    null));
            }
        }

        return rows;
    }

    private static bool IsFileHeader(string line)
    {
        return line.StartsWith("diff --git ", StringComparison.Ordinal) ||
               line.StartsWith("index ", StringComparison.Ordinal) ||
               line.StartsWith("--- ", StringComparison.Ordinal) ||
               line.StartsWith("+++ ", StringComparison.Ordinal) ||
               line.StartsWith("new file mode ", StringComparison.Ordinal) ||
               line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
               line.StartsWith("similarity index ", StringComparison.Ordinal) ||
               line.StartsWith("rename from ", StringComparison.Ordinal) ||
               line.StartsWith("rename to ", StringComparison.Ordinal) ||
               line.StartsWith("Binary files ", StringComparison.Ordinal);
    }

    private static string StripPrefix(string line) => line.Length == 0 ? string.Empty : line[1..];

    [GeneratedRegex("^@@ -(?<old>\\d+)(?:,\\d+)? \\+(?<new>\\d+)(?:,\\d+)? @@")]
    private static partial Regex HunkHeaderRegex();
}
