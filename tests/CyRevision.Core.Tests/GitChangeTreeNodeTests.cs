using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitChangeTreeNodeTests
{
    [Fact]
    public void FlatGroupShowsFilesDirectlyWithoutDirectoryHierarchy()
    {
        GitChangeViewModel[] changes =
        [
            Change("Source/Feature/First.cs"),
            Change("Plugins/Tool/Second.cs")
        ];

        GitChangeTreeNode group = GitChangeTreeNode.CreateFlatGroup("Versioned files", "tracked", changes);
        group.EnsureChildrenLoaded();

        Assert.True(group.IsExpanded);
        Assert.Equal(2, group.Children.Count);
        Assert.All(group.Children, child => Assert.False(child.IsDirectory));
        Assert.Equal(["First.cs", "Second.cs"], group.Children.Select(child => child.Name));
    }

    [Fact]
    public void FlatGroupLoadsLargeListsInCompactPages()
    {
        GitChangeViewModel[] changes = Enumerable.Range(0, 1200)
            .Select(index => Change($"Generated/File{index:D4}.txt"))
            .ToArray();
        GitChangeTreeNode group = GitChangeTreeNode.CreateFlatGroup("Unversioned files", "untracked", changes);

        group.EnsureChildrenLoaded();
        Assert.Equal(501, group.Children.Count);
        Assert.True(group.Children[^1].IsPlaceholder);

        group.EnsureChildrenLoaded();
        Assert.Equal(1001, group.Children.Count);
        Assert.True(group.Children[^1].IsPlaceholder);

        group.EnsureChildrenLoaded();
        Assert.Equal(1200, group.Children.Count);
        Assert.DoesNotContain(group.Children, child => child.IsPlaceholder);
    }

    [Fact]
    public void FlatGroupCanStartCollapsedWithoutMaterializingItsFiles()
    {
        GitChangeViewModel[] changes = [Change("Local/Private.txt")];

        GitChangeTreeNode group = GitChangeTreeNode.CreateFlatGroup(
            "Local-only files",
            "local",
            changes,
            isExpanded: false);

        Assert.False(group.IsExpanded);
        Assert.Single(group.Children);
        Assert.True(group.Children[0].IsPlaceholder);
        Assert.Equal(1, group.LeafCount);
    }

    [Fact]
    public void RefreshIncludedStateNotifiesLoadedFlatGroupAndFiles()
    {
        GitChangeViewModel[] changes =
        [
            Change("Generated/First.txt"),
            Change("Generated/Second.txt")
        ];
        GitChangeTreeNode group = GitChangeTreeNode.CreateFlatGroup("Unversioned files", "untracked", changes);
        group.EnsureChildrenLoaded();
        List<GitChangeTreeNode> notified = [];
        group.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GitChangeTreeNode.IsIncluded)) notified.Add(group);
        };
        foreach (GitChangeTreeNode child in group.Children)
        {
            child.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GitChangeTreeNode.IsIncluded)) notified.Add(child);
            };
        }

        foreach (GitChangeViewModel change in changes) change.IsIncluded = true;
        group.RefreshIncludedState();

        Assert.True(group.IsIncluded);
        Assert.All(group.Children, child => Assert.True(child.IsIncluded));
        Assert.Contains(group, notified);
        Assert.All(group.Children, child => Assert.Contains(child, notified));
    }

    private static GitChangeViewModel Change(string path) => new(
        new GitChange(path, GitChangeKind.Untracked, false, false));
}
