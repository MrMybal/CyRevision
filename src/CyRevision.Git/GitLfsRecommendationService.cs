namespace CyRevision.Git;

public sealed record GitLfsPatternRecommendation(
    string Pattern,
    string Category,
    string Reason,
    bool IsRecommended);

public sealed record GitLfsRecommendation(
    IReadOnlyList<string> DetectedProjectTypes,
    IReadOnlyList<GitLfsPatternRecommendation> Patterns)
{
    public string DetectionSummary => DetectedProjectTypes.Count == 0
        ? "Generic project"
        : string.Join(" + ", DetectedProjectTypes);
}

public static class GitLfsRecommendationService
{
    public static GitLfsRecommendation Build(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("A project root is required.", nameof(projectRoot));

        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        List<string> detected = [];
        List<GitLfsPatternRecommendation> patterns = [];
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);

        void Add(string pattern, string category, string reason, bool recommended = true)
        {
            if (emitted.Add(pattern))
                patterns.Add(new GitLfsPatternRecommendation(pattern, category, reason, recommended));
        }

        if (HasTopLevelFile(root, "*.uproject"))
        {
            detected.Add("Unreal Engine");
            Add("*.uasset", "Unreal Engine", "Binary Unreal asset package.");
            Add("*.umap", "Unreal Engine", "Binary Unreal map package.");
            Add("*.ubulk", "Unreal Engine", "Bulk Unreal asset payload.");
            Add("*.uexp", "Unreal Engine", "Exported Unreal package payload.");
        }

        if (Directory.Exists(Path.Combine(root, "Assets")) &&
            Directory.Exists(Path.Combine(root, "ProjectSettings")))
        {
            detected.Add("Unity");
            Add("*.unitypackage", "Unity", "Binary Unity package archive.");
        }

        if (File.Exists(Path.Combine(root, "project.godot")))
        {
            detected.Add("Godot");
        }

        // Source assets are useful defaults for game projects. Text-based engine files
        // (.unity, .prefab, .meta, .tscn, .tres) deliberately remain normal Git files.
        bool gameProject = detected.Contains("Unreal Engine") || detected.Contains("Unity") || detected.Contains("Godot");
        Add("*.fbx", "3D", "Binary interchange mesh and animation.", gameProject);
        Add("*.blend", "3D", "Blender source scene.", gameProject);
        Add("*.abc", "3D", "Alembic geometry cache.", gameProject);
        Add("*.usd", "3D", "USD scene or asset.", gameProject);
        Add("*.usdc", "3D", "Binary USD crate.", gameProject);
        Add("*.psd", "Images", "Layered Photoshop source image.", gameProject);
        Add("*.exr", "Images", "High dynamic-range image.", gameProject);
        Add("*.tga", "Images", "Common uncompressed game texture.", gameProject);
        Add("*.tif", "Images", "High-quality source texture.", false);
        Add("*.tiff", "Images", "High-quality source texture.", false);
        Add("*.wav", "Audio", "Uncompressed audio source.", gameProject);
        Add("*.flac", "Audio", "Lossless audio source.", false);
        Add("*.mp4", "Video", "Binary video asset.", gameProject);
        Add("*.mov", "Video", "Binary video asset.", gameProject);
        Add("*.zip", "Archives", "Binary archive; changes cannot be merged.", false);
        Add("*.7z", "Archives", "Binary archive; changes cannot be merged.", false);

        return new GitLfsRecommendation(detected, patterns);
    }

    public static string BuildAttributesContent(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        string[] normalized = patterns
            .Select(GitAttributesService.NormalizePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join('\n', normalized.Select(pattern =>
            $"{pattern} filter=lfs diff=lfs merge=lfs -text"));
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
