using System.IO.Enumeration;

namespace CyRevision.Code;

public static class CodeFilePatternMatcher
{
    public static bool IsMatch(string relativePath, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        string path = Normalize(relativePath);
        string fileName = Path.GetFileName(path);
        string[] patterns = expression.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return patterns.Length == 0 || patterns.Any(pattern => IsSingleMatch(path, fileName, Normalize(pattern)));
    }

    private static bool IsSingleMatch(string path, string fileName, string pattern)
    {
        if (pattern.Length == 0) return true;
        bool hasWildcard = pattern.IndexOfAny(['*', '?']) >= 0;
        if (!hasWildcard)
        {
            if (pattern.StartsWith(".", StringComparison.Ordinal) && !pattern.Contains('/'))
                return fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
            return path.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true) ||
            FileSystemName.MatchesSimpleExpression(pattern, path, ignoreCase: true))
            return true;

        string anywherePattern = pattern[0] == '*' ? pattern : $"*{pattern}";
        return FileSystemName.MatchesSimpleExpression(anywherePattern, path, ignoreCase: true);
    }

    private static string Normalize(string value) => value.Trim().Replace('\\', '/');
}
