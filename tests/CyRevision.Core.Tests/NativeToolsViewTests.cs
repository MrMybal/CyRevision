using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using CyRevision.Code;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class NativeToolsViewTests
{
    [Fact]
    public void EveryAuthoredFieldBindsToTheExistingApplicationModel()
    {
        string source = Source();
        HashSet<string> paths = [];
        foreach (Match match in Regex.Matches(source, "(?<![.a-zA-Z])(?:field|summary|flag|value|textPanel)\\(\"(?<name>[A-Z][a-zA-Z0-9.]*)\""))
            paths.Add(match.Groups["name"].Value);
        foreach (Match block in Regex.Matches(source, "fields\\(\\{(?<rows>[\\s\\S]*?)\\}\\);"))
            foreach (Match match in Regex.Matches(block.Groups["rows"].Value, "\\{\"(?<name>[A-Z][a-zA-Z0-9.]*)\","))
                paths.Add(match.Groups["name"].Value);
        foreach (Match match in Regex.Matches(source, "selector\\(\"(?<source>[^\"]+)\",\"(?<selection>[^\"]+)\""))
        {
            paths.Add(match.Groups["source"].Value);
            paths.Add(match.Groups["selection"].Value);
        }
        Assert.True(paths.Count >= 250, "The native tools must expose the complete authored configuration, not a reduced placeholder.");
        foreach (string path in paths)
        {
            Type type = typeof(MainWindowViewModel);
            foreach (string part in path.Split('.'))
            {
                PropertyInfo? property = type.GetProperty(part, BindingFlags.Instance | BindingFlags.Public);
                Assert.True(property is not null, $"Unknown native field {path} ({type.Name}.{part}).");
                type = property!.PropertyType;
            }
        }
    }

    [Fact]
    public void EveryAuthoredActionUsesAnActualSharedEngineHandler()
    {
        string source = Source();
        HashSet<string> names = [];
        foreach (Match match in Regex.Matches(source, "(?<![.a-zA-Z])command\\(\"(?<name>[A-Za-z0-9]+)\"")) names.Add(match.Groups["name"].Value);
        foreach (Match block in Regex.Matches(source, "commands\\(\\{(?<rows>[\\s\\S]*?)\\}\\);"))
            foreach (Match match in Regex.Matches(block.Groups["rows"].Value, "\\{\"(?<name>[A-Za-z0-9]+)\",")) names.Add(match.Groups["name"].Value);
        foreach (Match match in Regex.Matches(source, "(?:choose|parameterButton)\\(\"[^\"]*\",\"(?<name>[A-Za-z0-9]+)\"")) names.Add(match.Groups["name"].Value);
        Assert.True(names.Count >= 130);
        foreach (string name in names)
        {
            MethodInfo[] methods = typeof(MainWindowViewModel).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == name).ToArray();
            Assert.Single(methods);
            MethodInfo method = methods[0];
            Assert.True(method.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(method.ReturnType), name);
            Assert.All(method.GetParameters(), parameter => Assert.True(parameter.ParameterType == typeof(CancellationToken) || NativeWorkspaceCatalog.IsScalar(parameter.ParameterType), name));
        }
    }

    [Fact]
    public void EveryAuthoredListHasAnActualSource()
    {
        string source = Source();
        foreach (Match match in Regex.Matches(source, "(?<![.a-zA-Z])list\\(\"(?<source>[^\"]+)\""))
        {
            string name = match.Groups["source"].Value;
            if (name is "NativeSolutionTreeRows" or "NativeFileHistory") continue;
            PropertyInfo? property = typeof(MainWindowViewModel).GetProperty(name);
            Assert.NotNull(property);
            Assert.True(typeof(IEnumerable).IsAssignableFrom(property!.PropertyType), name);
        }
    }

    [Fact]
    public void FolderRowsKeepTheirActualNodesAndSafeIdentity()
    {
        CodeTreeNode node = new("Source", "Source", "fixture/Source", true, hasUnloadedChildren: true);
        NativeToolsActions.NativeSolutionRow row = new(node);
        Assert.Equal("Source", row.Name);
        Assert.True(row.IsDirectory);
        Assert.False(row.Expanded);
        Assert.Equal("Source", row.RelativePath);
        Assert.Same(row, NativeToolsActions.RequireTreeRow([row], "Source", NativeTable.Key(row)));
        Assert.Throws<InvalidOperationException>(() => NativeToolsActions.RequireTreeRow([row], "../outside", NativeTable.Key(row)));
        NativeToolsActions.NativeSolutionRow replacement = new(node);
        Assert.Throws<InvalidOperationException>(() => NativeToolsActions.RequireTreeRow([replacement], "Source", NativeTable.Key(row)));
    }

    [Fact]
    public void ToolsRoutingDoesNotExposeArbitraryProcessOrFilesystemCommands()
    {
        Assert.True(NativeToolsActions.Handles("tools.tree.toggle"));
        Assert.True(NativeToolsActions.Handles("tools.saveSyncVersionSettings"));
        Assert.False(NativeToolsActions.Handles("tools.delete"));
        Assert.False(NativeToolsActions.Handles("tools.execute"));
        Assert.False(NativeToolsActions.Handles(""));
    }

    [Fact]
    public void FileHistoryProjectsTheNestedRevisionAndLfsMetadata()
    {
        GitRevision commit = new("full-hash", "short", "Example author", "example@invalid.test", DateTimeOffset.Parse("2026-01-02T03:04:00Z"), "Update fixture asset");
        GitFileRevision entry = new(commit, "Content/Fixture.uasset", GitChangeKind.Modified, 2, 2, true, new("abc123", 1234));
        IList projected = NativeToolsActions.ProjectFileHistory([entry]);
        NativeToolsActions.FileHistoryRow row = Assert.IsType<NativeToolsActions.FileHistoryRow>(Assert.Single(projected.Cast<object>()));
        Assert.Equal("full-hash", row.CommitHash);
        Assert.Equal("short", row.Commit);
        Assert.Equal("Example author", row.Author);
        Assert.Equal(commit.AuthoredAt, row.Date);
        Assert.Equal("abc123", row.LfsOid);
        Assert.Equal(1234L, row.Size);
        Assert.Equal("Git LFS", row.Storage);
        Assert.Equal(commit.Subject, row.Message);
    }

    private static string Source()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "native", "CyRevision.Native", "ToolsViews.cpp");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new InvalidOperationException("Run native view binding tests from the repository checkout.");
    }
}
