using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace CyRevision.Diff;

public sealed partial class AssetDiffService : IAssetDiffService
{
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".csv",
        ".cs", ".cpp", ".h", ".hpp", ".usf", ".ush", ".py", ".js", ".ts", ".tsx"
    };

    public async Task<AssetDiffResult> CompareAsync(
        string baselinePath,
        string candidatePath,
        string artifactDirectory,
        CancellationToken cancellationToken = default)
    {
        string baseline = RequireFile(baselinePath);
        string candidate = RequireFile(candidatePath);
        string extension = Path.GetExtension(candidate);
        Directory.CreateDirectory(artifactDirectory);

        if (TextureExtensions.Contains(extension) &&
            string.Equals(Path.GetExtension(baseline), extension, StringComparison.OrdinalIgnoreCase))
        {
            return await CompareTexturesAsync(baseline, candidate, artifactDirectory, cancellationToken);
        }

        if (string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase))
        {
            return await CompareObjMeshesAsync(baseline, candidate, artifactDirectory, cancellationToken);
        }

        if (string.Equals(extension, ".uasset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".umap", StringComparison.OrdinalIgnoreCase))
        {
            return await CompareUnrealPackagesAsync(baseline, candidate, cancellationToken);
        }

        if (TextExtensions.Contains(extension))
        {
            return await CompareTextAsync(baseline, candidate, cancellationToken);
        }

        return await CompareBinaryAsync(baseline, candidate, AssetDiffKind.Binary, cancellationToken);
    }

    public async Task<UnrealDependencyGraph> ScanUnrealDependenciesAsync(
        string projectRoot,
        int maximumAssetCount = 500,
        CancellationToken cancellationToken = default)
    {
        if (maximumAssetCount is < 10 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAssetCount));
        }

        string root = Path.GetFullPath(projectRoot);
        string contentRoot = Path.Combine(root, "Content");
        if (!Directory.Exists(contentRoot))
        {
            return new UnrealDependencyGraph([], [], 0, 0, 0);
        }

        string[] allAssets = Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".uasset" or ".umap")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] inspectedAssets = allAssets.Take(maximumAssetCount).ToArray();
        Dictionary<string, string> packagePaths = inspectedAssets.ToDictionary(
            path => GetUnrealPackageName(contentRoot, path),
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> outgoing = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> incoming = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> assetTypes = new(StringComparer.OrdinalIgnoreCase);
        List<UnrealAssetDependency> dependencies = [];
        int unresolvedReferences = 0;

        foreach (string assetPath in inspectedAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(root, assetPath).Replace('\\', '/');
            (HashSet<string> References, string AssetType) inspection = await InspectUnrealReferencesAsync(
                assetPath,
                cancellationToken);
            assetTypes[relativePath] = inspection.AssetType;
            HashSet<string> resolvedTargets = new(StringComparer.OrdinalIgnoreCase);
            outgoing[relativePath] = resolvedTargets;
            foreach (string packageReference in inspection.References)
            {
                if (!packagePaths.TryGetValue(packageReference, out string? targetPath))
                {
                    unresolvedReferences++;
                    continue;
                }

                if (string.Equals(relativePath, targetPath, StringComparison.OrdinalIgnoreCase) ||
                    !resolvedTargets.Add(targetPath))
                {
                    continue;
                }

                incoming[targetPath] = incoming.GetValueOrDefault(targetPath) + 1;
                dependencies.Add(new UnrealAssetDependency(relativePath, targetPath, packageReference));
            }
        }

        UnrealAssetNode[] nodes = inspectedAssets.Select(path =>
        {
            string relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            return new UnrealAssetNode(
                relativePath,
                GetUnrealPackageName(contentRoot, path),
                assetTypes.GetValueOrDefault(relativePath, Path.GetExtension(path) == ".umap" ? "World" : "Asset"),
                new FileInfo(path).Length,
                outgoing.GetValueOrDefault(relativePath)?.Count ?? 0,
                incoming.GetValueOrDefault(relativePath));
        }).ToArray();
        return new UnrealDependencyGraph(
            nodes,
            dependencies,
            allAssets.Length,
            inspectedAssets.Length,
            unresolvedReferences);
    }

    private static async Task<(HashSet<string> References, string AssetType)> InspectUnrealReferencesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        int length = (int)Math.Min(file.Length, 8 * 1024 * 1024);
        byte[] buffer = new byte[length];
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        int read = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken);
        string ascii = Encoding.ASCII.GetString(buffer, 0, read);
        string unicode = Encoding.Unicode.GetString(buffer, 0, read - read % 2);
        HashSet<string> references = UnrealPackageReferenceRegex().Matches(ascii)
            .Concat(UnrealPackageReferenceRegex().Matches(unicode))
            .Select(match => NormalizeUnrealPackageReference(match.Value))
            .Where(value => value.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] typeHints = ["Blueprint", "Texture2D", "StaticMesh", "SkeletalMesh", "MaterialInstance", "Material", "World", "Niagara", "SoundWave"];
        string assetType = typeHints.FirstOrDefault(type =>
            ascii.Contains(type, StringComparison.OrdinalIgnoreCase) ||
            unicode.Contains(type, StringComparison.OrdinalIgnoreCase))
            ?? (Path.GetExtension(path) == ".umap" ? "World" : "Asset");
        return (references, assetType);
    }

    private static string GetUnrealPackageName(string contentRoot, string assetPath)
    {
        string relative = Path.GetRelativePath(contentRoot, assetPath).Replace('\\', '/');
        return "/Game/" + relative[..^Path.GetExtension(relative).Length];
    }

    private static string NormalizeUnrealPackageReference(string value)
    {
        string reference = value.TrimEnd('\0', '\'', '"', ',', ';', ')', ']', '}');
        int lastSlash = reference.LastIndexOf('/');
        int objectSeparator = reference.IndexOf('.', Math.Max(0, lastSlash));
        return objectSeparator > lastSlash ? reference[..objectSeparator] : reference;
    }

    private static async Task<AssetDiffResult> CompareTexturesAsync(
        string baseline,
        string candidate,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        using SKBitmap baselineBitmap = SKBitmap.Decode(baseline)
                                        ?? throw new InvalidDataException("La texture de référence ne peut pas être décodée.");
        using SKBitmap candidateBitmap = SKBitmap.Decode(candidate)
                                         ?? throw new InvalidDataException("La texture candidate ne peut pas être décodée.");
        int width = Math.Max(baselineBitmap.Width, candidateBitmap.Width);
        int height = Math.Max(baselineBitmap.Height, candidateBitmap.Height);
        using SKBitmap heatmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        long changedPixels = 0;
        double differenceTotal = 0;

        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                bool insideBaseline = x < baselineBitmap.Width && y < baselineBitmap.Height;
                bool insideCandidate = x < candidateBitmap.Width && y < candidateBitmap.Height;
                SKColor left = insideBaseline ? baselineBitmap.GetPixel(x, y) : SKColors.Transparent;
                SKColor right = insideCandidate ? candidateBitmap.GetPixel(x, y) : SKColors.Transparent;
                int red = Math.Abs(left.Red - right.Red);
                int green = Math.Abs(left.Green - right.Green);
                int blue = Math.Abs(left.Blue - right.Blue);
                int alpha = Math.Abs(left.Alpha - right.Alpha);
                int difference = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                if (difference > 0)
                {
                    changedPixels++;
                }

                differenceTotal += difference / 255d;
                heatmap.SetPixel(x, y, difference == 0
                    ? new SKColor(14, 21, 38, 255)
                    : new SKColor((byte)Math.Max(45, difference), (byte)(difference / 5), (byte)(difference / 8), 255));
            }
        }

        string previewPath = Path.Combine(artifactDirectory, $"texture-diff-{Guid.NewGuid():N}.png");
        using SKImage preview = SKImage.FromBitmap(heatmap);
        using SKData encoded = preview.Encode(SKEncodedImageFormat.Png, 100);
        await using (FileStream output = new(previewPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            encoded.SaveTo(output);
            await output.FlushAsync(cancellationToken);
        }

        long totalPixels = (long)width * height;
        double changedPercent = totalPixels == 0 ? 0 : changedPixels * 100d / totalPixels;
        double meanDifference = totalPixels == 0 ? 0 : differenceTotal * 100d / totalPixels;
        bool equivalent = changedPixels == 0 && baselineBitmap.Width == candidateBitmap.Width && baselineBitmap.Height == candidateBitmap.Height;
        return new AssetDiffResult(
            AssetDiffKind.Texture,
            equivalent,
            equivalent ? "Textures identiques" : $"{changedPercent:0.##}% des pixels diffèrent",
            new Dictionary<string, string>
            {
                ["Référence"] = $"{baselineBitmap.Width} × {baselineBitmap.Height}",
                ["Candidate"] = $"{candidateBitmap.Width} × {candidateBitmap.Height}",
                ["Pixels modifiés"] = $"{changedPixels:N0} / {totalPixels:N0}",
                ["Écart moyen"] = $"{meanDifference:0.##}%"
            },
            ["La heatmap rouge représente l'intensité maximale de différence RGBA par pixel."],
            previewPath);
    }

    private static async Task<AssetDiffResult> CompareObjMeshesAsync(
        string baseline,
        string candidate,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        ObjGeometry leftGeometry = await ReadObjGeometryAsync(baseline, cancellationToken);
        ObjGeometry rightGeometry = await ReadObjGeometryAsync(candidate, cancellationToken);
        ObjStatistics left = leftGeometry.Statistics;
        ObjStatistics right = rightGeometry.Statistics;
        bool equivalent = left == right && await FilesEqualAsync(baseline, candidate, cancellationToken);
        List<string> details = [];
        AddDelta(details, "sommets", left.Vertices, right.Vertices);
        AddDelta(details, "faces", left.Faces, right.Faces);
        AddDelta(details, "UV", left.TextureCoordinates, right.TextureCoordinates);
        AddDelta(details, "normales", left.Normals, right.Normals);
        string previewPath = Path.Combine(artifactDirectory, $"mesh-overlay-{Guid.NewGuid():N}.png");
        RenderMeshOverlay(leftGeometry, rightGeometry, previewPath);
        return new AssetDiffResult(
            AssetDiffKind.ObjMesh,
            equivalent,
            equivalent ? "Meshes OBJ identiques" : "La topologie ou les attributs du mesh ont changé",
            new Dictionary<string, string>
            {
                ["Sommets"] = $"{left.Vertices:N0} → {right.Vertices:N0}",
                ["Faces"] = $"{left.Faces:N0} → {right.Faces:N0}",
                ["UV"] = $"{left.TextureCoordinates:N0} → {right.TextureCoordinates:N0}",
                ["Normales"] = $"{left.Normals:N0} → {right.Normals:N0}",
                ["Bounds référence"] = left.Bounds,
                ["Bounds candidat"] = right.Bounds
            },
            details.Count == 0 ? ["Les statistiques correspondent, mais les données ou l'ordre des éléments diffèrent."] : details,
            previewPath);
    }

    private static async Task<AssetDiffResult> CompareUnrealPackagesAsync(
        string baseline,
        string candidate,
        CancellationToken cancellationToken)
    {
        UnrealPackageSummary left = await ReadUnrealSummaryAsync(baseline, cancellationToken);
        UnrealPackageSummary right = await ReadUnrealSummaryAsync(candidate, cancellationToken);
        BinaryBlockDifference blocks = await CompareBlocksAsync(baseline, candidate, cancellationToken);
        string[] addedSymbols = right.Symbols.Except(left.Symbols, StringComparer.Ordinal).Take(80).ToArray();
        string[] removedSymbols = left.Symbols.Except(right.Symbols, StringComparer.Ordinal).Take(80).ToArray();
        List<string> details = [];
        details.AddRange(addedSymbols.Select(symbol => $"+ symbole : {symbol}"));
        details.AddRange(removedSymbols.Select(symbol => $"− symbole : {symbol}"));
        if (details.Count == 0 && blocks.ChangedBlocks > 0)
        {
            details.Add("La structure binaire a changé sans modification de symbole ASCII détectable.");
        }

        bool equivalent = blocks.ChangedBlocks == 0 && blocks.LeftHash == blocks.RightHash;
        return new AssetDiffResult(
            AssetDiffKind.UnrealPackage,
            equivalent,
            equivalent ? "Packages Unreal identiques" : "Package Unreal modifié — analyse hors moteur simplifiée",
            new Dictionary<string, string>
            {
                ["Signature UE référence"] = left.HasUnrealMagic ? "détectée" : "non détectée",
                ["Signature UE candidate"] = right.HasUnrealMagic ? "détectée" : "non détectée",
                ["Taille"] = $"{left.Length:N0} → {right.Length:N0} octets",
                ["Blocs modifiés"] = $"{blocks.ChangedBlocks:N0} / {blocks.TotalBlocks:N0}",
                ["Symboles lisibles"] = $"{left.Symbols.Count:N0} → {right.Symbols.Count:N0}",
                ["Types probables"] = string.Join(", ", right.LikelyTypes.DefaultIfEmpty("non déterminé"))
            },
            details);
    }

    private static async Task<AssetDiffResult> CompareTextAsync(
        string baseline,
        string candidate,
        CancellationToken cancellationToken)
    {
        string[] left = await File.ReadAllLinesAsync(baseline, cancellationToken);
        string[] right = await File.ReadAllLinesAsync(candidate, cancellationToken);
        HashSet<string> leftLines = left.ToHashSet(StringComparer.Ordinal);
        HashSet<string> rightLines = right.ToHashSet(StringComparer.Ordinal);
        string[] added = right.Where(line => !leftLines.Contains(line)).Take(100).ToArray();
        string[] removed = left.Where(line => !rightLines.Contains(line)).Take(100).ToArray();
        bool equivalent = left.SequenceEqual(right, StringComparer.Ordinal);
        List<string> details = removed.Select(line => "− " + line).Concat(added.Select(line => "+ " + line)).ToList();
        return new AssetDiffResult(
            AssetDiffKind.Text,
            equivalent,
            equivalent ? "Fichiers texte identiques" : $"{added.Length} ajout(s), {removed.Length} retrait(s) affiché(s)",
            new Dictionary<string, string>
            {
                ["Lignes"] = $"{left.Length:N0} → {right.Length:N0}",
                ["Encodage"] = "UTF-8 / détection .NET"
            },
            details);
    }

    private static async Task<AssetDiffResult> CompareBinaryAsync(
        string baseline,
        string candidate,
        AssetDiffKind kind,
        CancellationToken cancellationToken)
    {
        BinaryBlockDifference blocks = await CompareBlocksAsync(baseline, candidate, cancellationToken);
        bool equivalent = blocks.LeftHash == blocks.RightHash;
        return new AssetDiffResult(
            kind,
            equivalent,
            equivalent ? "Fichiers binaires identiques" : "Contenu binaire différent",
            new Dictionary<string, string>
            {
                ["SHA-256 référence"] = blocks.LeftHash,
                ["SHA-256 candidat"] = blocks.RightHash,
                ["Blocs modifiés"] = $"{blocks.ChangedBlocks:N0} / {blocks.TotalBlocks:N0}"
            },
            []);
    }

    private static async Task<ObjGeometry> ReadObjGeometryAsync(string path, CancellationToken cancellationToken)
    {
        List<Vector3> points = [];
        List<int[]> faceIndices = [];
        int textureCoordinates = 0, normals = 0;
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    Vector3 point = new(x, y, z);
                    points.Add(point);
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                int[] indices = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1)
                    .Select(value => value.Split('/')[0])
                    .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                        ? index > 0 ? index - 1 : points.Count + index
                        : -1)
                    .Where(index => index >= 0 && index < points.Count)
                    .ToArray();
                if (indices.Length >= 2) faceIndices.Add(indices);
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal)) textureCoordinates++;
            else if (line.StartsWith("vn ", StringComparison.Ordinal)) normals++;
        }

        string bounds = points.Count == 0 ? "aucun sommet" : $"[{minimum.X:0.###}, {minimum.Y:0.###}, {minimum.Z:0.###}] → [{maximum.X:0.###}, {maximum.Y:0.###}, {maximum.Z:0.###}]";
        return new ObjGeometry(
            points,
            faceIndices,
            new ObjStatistics(points.Count, faceIndices.Count, textureCoordinates, normals, bounds));
    }

    private static void RenderMeshOverlay(ObjGeometry baseline, ObjGeometry candidate, string outputPath)
    {
        const int width = 1200;
        const int height = 800;
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(new SKColor(10, 16, 30));
        List<SKPoint> allProjected = baseline.Points.Concat(candidate.Points).Select(ProjectPoint).ToList();
        if (allProjected.Count == 0)
        {
            using SKImage emptyImage = SKImage.FromBitmap(bitmap);
            using SKData emptyData = emptyImage.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream emptyOutput = File.Create(outputPath);
            emptyData.SaveTo(emptyOutput);
            return;
        }

        float minX = allProjected.Min(point => point.X), maxX = allProjected.Max(point => point.X);
        float minY = allProjected.Min(point => point.Y), maxY = allProjected.Max(point => point.Y);
        float scale = Math.Min((width - 100) / Math.Max(0.001f, maxX - minX), (height - 100) / Math.Max(0.001f, maxY - minY));
        SKPoint Transform(Vector3 point)
        {
            SKPoint projected = ProjectPoint(point);
            return new SKPoint(50 + (projected.X - minX) * scale, height - 50 - (projected.Y - minY) * scale);
        }

        DrawGeometry(canvas, baseline, Transform, new SKColor(70, 210, 236, 180));
        DrawGeometry(canvas, candidate, Transform, new SKColor(244, 87, 178, 180));
        using SKPaint labelPaint = new() { Color = SKColors.White, IsAntialias = true };
        using SKFont labelFont = new(SKTypeface.Default, 22);
        canvas.DrawText("Référence", 24, 34, SKTextAlign.Left, labelFont, labelPaint);
        labelPaint.Color = new SKColor(70, 210, 236);
        canvas.DrawRect(145, 17, 24, 17, labelPaint);
        labelPaint.Color = SKColors.White;
        canvas.DrawText("Candidat", 205, 34, SKTextAlign.Left, labelFont, labelPaint);
        labelPaint.Color = new SKColor(244, 87, 178);
        canvas.DrawRect(310, 17, 24, 17, labelPaint);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(outputPath);
        data.SaveTo(output);
    }

    private static void DrawGeometry(
        SKCanvas canvas,
        ObjGeometry geometry,
        Func<Vector3, SKPoint> transform,
        SKColor color)
    {
        using SKPaint paint = new() { Color = color, StrokeWidth = 1.35f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        int drawnEdges = 0;
        foreach (int[] face in geometry.Faces)
        {
            for (int index = 0; index < face.Length && drawnEdges < 300_000; index++, drawnEdges++)
            {
                Vector3 start = geometry.Points[face[index]];
                Vector3 end = geometry.Points[face[(index + 1) % face.Length]];
                canvas.DrawLine(transform(start), transform(end), paint);
            }

            if (drawnEdges >= 300_000) break;
        }
    }

    private static SKPoint ProjectPoint(Vector3 point) => new(
        (point.X - point.Z) * 0.70710677f,
        point.Y * 0.8164966f - (point.X + point.Z) * 0.4082483f);

    private static async Task<UnrealPackageSummary> ReadUnrealSummaryAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        int length = (int)Math.Min(file.Length, 4 * 1024 * 1024);
        byte[] buffer = new byte[length];
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        int read = await stream.ReadAsync(buffer, cancellationToken);
        bool magic = read >= 4 && BitConverter.ToUInt32(buffer, 0) == 0x9E2A83C1;
        string text = Encoding.ASCII.GetString(buffer, 0, read);
        HashSet<string> symbols = AsciiSymbolRegex().Matches(text)
            .Select(match => match.Value)
            .Where(value => value.Any(char.IsLetter))
            .Take(5000)
            .ToHashSet(StringComparer.Ordinal);
        string[] typeHints = ["Blueprint", "Texture2D", "StaticMesh", "SkeletalMesh", "Material", "World", "Niagara", "SoundWave"];
        string[] likelyTypes = typeHints.Where(type => symbols.Any(symbol => symbol.Contains(type, StringComparison.OrdinalIgnoreCase))).ToArray();
        return new UnrealPackageSummary(file.Length, magic, symbols, likelyTypes);
    }

    private static async Task<BinaryBlockDifference> CompareBlocksAsync(
        string baseline,
        string candidate,
        CancellationToken cancellationToken)
    {
        const int blockSize = 64 * 1024;
        await using FileStream left = new(baseline, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, true);
        await using FileStream right = new(candidate, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, true);
        using IncrementalHash leftHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash rightHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] leftBuffer = new byte[blockSize];
        byte[] rightBuffer = new byte[blockSize];
        int changed = 0, total = 0;
        while (true)
        {
            int leftRead = await left.ReadAsync(leftBuffer, cancellationToken);
            int rightRead = await right.ReadAsync(rightBuffer, cancellationToken);
            if (leftRead == 0 && rightRead == 0) break;
            total++;
            leftHash.AppendData(leftBuffer, 0, leftRead);
            rightHash.AppendData(rightBuffer, 0, rightRead);
            if (leftRead != rightRead || !leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead))) changed++;
        }

        return new BinaryBlockDifference(
            Convert.ToHexString(leftHash.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(rightHash.GetHashAndReset()).ToLowerInvariant(),
            changed,
            total);
    }

    private static async Task<bool> FilesEqualAsync(string left, string right, CancellationToken cancellationToken) =>
        (await CompareBlocksAsync(left, right, cancellationToken)).ChangedBlocks == 0;

    private static void AddDelta(List<string> details, string label, int left, int right)
    {
        if (left != right) details.Add($"{label} : {left:N0} → {right:N0} ({right - left:+#;-#;0})");
    }

    private static string RequireFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) ? fullPath : throw new FileNotFoundException("Le fichier de comparaison est introuvable.", fullPath);
    }

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_./:-]{3,127}", RegexOptions.CultureInvariant)]
    private static partial Regex AsciiSymbolRegex();

    [GeneratedRegex(@"/(?:Game|Engine)/[A-Za-z0-9_./-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnrealPackageReferenceRegex();

    private sealed record ObjStatistics(int Vertices, int Faces, int TextureCoordinates, int Normals, string Bounds);
    private sealed record ObjGeometry(IReadOnlyList<Vector3> Points, IReadOnlyList<int[]> Faces, ObjStatistics Statistics);
    private sealed record UnrealPackageSummary(long Length, bool HasUnrealMagic, IReadOnlySet<string> Symbols, IReadOnlyList<string> LikelyTypes);
    private sealed record BinaryBlockDifference(string LeftHash, string RightHash, int ChangedBlocks, int TotalBlocks);
}
