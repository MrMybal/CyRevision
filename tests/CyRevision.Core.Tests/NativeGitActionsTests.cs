using System.Collections;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CyRevision.Core.Tests;

public sealed class NativeGitActionsTests
{
    [Fact]
    public void ChangeProjectionKeepsCheckboxStateInSyncWithDescendants()
    {
        var first = Change("Content/First.uasset");
        var second = Change("Content/Second.uasset");
        var node = GitChangeTreeNode.CreateFlatGroup("Untracked files", "untracked", [first, second]);
        var row = new NativeGitActions.ChangeRow(node);
        Assert.Equal("Unversioned files", row.Name);
        Assert.False(row.IsIncluded);
        first.IsIncluded = true;
        Assert.Null(row.IsIncluded);
        second.IsIncluded = true;
        Assert.True(row.IsIncluded);
        Assert.Equal(2, row.Count);
    }

    [Fact]
    public void LocalProjectionIsCollapsedAndCannotBeIncluded()
    {
        var change = Change("Generated/local.tmp");
        change.IsLocalOnly = true;
        var node = GitChangeTreeNode.CreateFlatGroup("Local files", "local", [change], isExpanded: false);
        var row = new NativeGitActions.ChangeRow(node);
        Assert.True(row.IsLocalOnly);
        Assert.False(row.IsExpanded);
        Assert.False(row.CanInclude);
        change.IsIncluded = true;
        Assert.False(change.IsIncluded);
    }

    [Fact]
    public void StatePollingDoesNotLoadAnotherFilePageAfterInitialization()
    {
        GitChangeViewModel[] changes = Enumerable.Range(0, 1200).Select(index => Change($"File{index}.cpp")).ToArray();
        var group = GitChangeTreeNode.CreateFlatGroup("Untracked files", "untracked", changes);
        Assert.True(NativeGitActions.NeedsInitialLoad(group));
        group.EnsureChildrenLoaded();
        Assert.Equal(501, group.Children.Count);
        Assert.True(group.Children[^1].IsPlaceholder);
        for (int poll = 0; poll < 10; poll++)
            if (NativeGitActions.NeedsInitialLoad(group)) group.EnsureChildrenLoaded();
        Assert.Equal(501, group.Children.Count);
        Assert.False(NativeGitActions.NeedsInitialLoad(group));
        group.EnsureChildrenLoaded(); // Explicit load-more action.
        Assert.Equal(1001, group.Children.Count);
    }

    [Fact]
    public void SelectionResolvesIdentityAndPreservesOrder()
    {
        var first = Change("First.cpp");
        var second = Change("Second.cpp");
        IList list = new[] { first, second };
        var selected = NativeGitActions.ResolveKeys<GitChangeViewModel>(list,
            [NativeTable.Key(second), NativeTable.Key(first)]);
        Assert.Same(second, selected[0]);
        Assert.Same(first, selected[1]);
    }

    [Fact]
    public void SelectionRejectsAReplacedRowEvenIfItsPathMatches()
    {
        var original = Change("Asset.uasset");
        var replacement = Change("Asset.uasset");
        Assert.Throws<InvalidOperationException>(() => NativeGitActions.ResolveKeys<GitChangeViewModel>(
            new[] { replacement }, [NativeTable.Key(original)]));
        Assert.False(replacement.IsIncluded);
    }

    [Fact]
    public void SelectionRejectsDuplicatesAndWrongTypes()
    {
        var change = Change("First.cpp");
        string key = NativeTable.Key(change);
        Assert.Throws<ArgumentException>(() => NativeGitActions.ResolveKeys<GitChangeViewModel>(new[] { change }, [key, key]));
        Assert.Throws<InvalidOperationException>(() => NativeGitActions.ResolveKeys<GitBranch>(new[] { change }, [key]));
    }

    [Fact]
    public void NativeCatalogMapsEveryOriginalWorkspaceExactlyOnce()
    {
        string root = RepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "CyRevision.Desktop", "MainWindow.axaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] legacy = document.Descendants().Where(node => node.Name.LocalName == "TabItem")
            .Select(node => node.Attribute(xaml + "Name")?.Value)
            .Where(name => name?.EndsWith("WorkspaceTab", StringComparison.Ordinal) == true).Cast<string>().Order().ToArray();
        string[] native = NativeWorkspaceCatalog.All.Select(workspace => workspace.LoadTab).Order().ToArray();
        Assert.Equal(legacy, native);
        Assert.Equal(native.Length, native.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(NativeWorkspaceCatalog.All.Count, NativeWorkspaceCatalog.All.Select(workspace => workspace.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryGitButtonCallsAnAvailableScalarEngineAction()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "native", "CyRevision.Native", "GitViews.cpp"));
        string[] names = Regex.Matches(source, "command\\(\"([A-Za-z0-9_]+)\"")
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(names.Length >= 40);
        MethodInfo[] methods = typeof(MainWindowViewModel).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (string name in names)
        {
            MethodInfo method = Assert.Single(methods.Where(candidate => candidate.Name == name));
            Assert.True(method.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(method.ReturnType), name);
            Assert.All(method.GetParameters(), parameter => Assert.True(parameter.ParameterType == typeof(CancellationToken) ||
                NativeWorkspaceCatalog.IsScalar(parameter.ParameterType), $"{name}.{parameter.Name} needs a native interaction adapter"));
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "CyRevision.Desktop", "MainWindow.axaml"))) return directory.FullName;
        throw new InvalidOperationException("Run the native parity tests from the repository checkout.");
    }

    private static GitChangeViewModel Change(string path) => new(new GitChange(path, GitChangeKind.Untracked, false, false));
}
