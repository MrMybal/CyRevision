using System.Text;

namespace CyRevision.Git;

public sealed record GitIgnoreRecommendation(
    IReadOnlyList<string> DetectedProjectTypes,
    string Content)
{
    public string DetectionSummary => DetectedProjectTypes.Count == 0
        ? "Generic project"
        : string.Join(" + ", DetectedProjectTypes);
}

public static class GitIgnoreRecommendationService
{
    public static GitIgnoreRecommendation Build(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("A project root is required.", nameof(projectRoot));

        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        List<string> detected = [];
        List<(string Title, string[] Rules)> sections = [];

        if (HasTopLevelFile(root, "*.uproject"))
        {
            detected.Add("Unreal Engine");
            sections.Add(("Unreal Engine generated data",
            [
                "Binaries/",
                "DerivedDataCache/",
                "Intermediate/",
                "Saved/",
                "*.VC.db",
                "*.opensdf",
                "*.sdf",
                "*.suo"
            ]));
        }

        if (Directory.Exists(Path.Combine(root, "Assets")) &&
            Directory.Exists(Path.Combine(root, "ProjectSettings")))
        {
            detected.Add("Unity");
            sections.Add(("Unity generated data",
            [
                "/[Ll]ibrary/",
                "/[Tt]emp/",
                "/[Oo]bj/",
                "/[Bb]uild/",
                "/[Bb]uilds/",
                "/[Ll]ogs/",
                "/[Uu]ser[Ss]ettings/",
                "/MemoryCaptures/"
            ]));
        }

        if (File.Exists(Path.Combine(root, "project.godot")))
        {
            detected.Add("Godot");
            sections.Add(("Godot generated data", ["/.godot/", "/.import/"]));
        }

        if (HasTopLevelFile(root, "*.sln") || HasTopLevelFile(root, "*.csproj"))
        {
            detected.Add(".NET");
            sections.Add((".NET generated data",
            [
                "[Bb]in/",
                "[Oo]bj/",
                "TestResults/",
                "*.user",
                "*.suo"
            ]));
        }

        if (File.Exists(Path.Combine(root, "package.json")))
        {
            detected.Add("Node.js");
            sections.Add(("Node.js dependencies and logs",
            [
                "node_modules/",
                ".pnpm-store/",
                "npm-debug.log*",
                "yarn-debug.log*",
                "yarn-error.log*"
            ]));
        }

        sections.Add(("Local tools and IDE state",
        [
            ".cyrevision/",
            ".vs/",
            ".idea/",
            "_ReSharper.Caches/",
            "*.sln.iml"
        ]));
        sections.Add(("Operating-system metadata",
        [
            ".DS_Store",
            ".AppleDouble",
            "Thumbs.db",
            "Thumbs.db:encryptable",
            "ehthumbs.db",
            "Desktop.ini",
            "$RECYCLE.BIN/"
        ]));

        StringBuilder output = new();
        output.AppendLine("# Recommended by CyRevision — review before committing")
            .AppendLine("# Generated files and machine-local state only.")
            .AppendLine();
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string title, string[] rules) in sections)
        {
            string[] unique = rules.Where(emitted.Add).ToArray();
            if (unique.Length == 0) continue;
            output.Append("# ").AppendLine(title);
            foreach (string rule in unique) output.AppendLine(rule);
            output.AppendLine();
        }

        return new GitIgnoreRecommendation(detected, output.ToString().Replace("\r\n", "\n"));
    }

    private static bool HasTopLevelFile(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
