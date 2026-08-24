using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitRevisionTreeNodeTests
{
    [Fact]
    public void TreeGroupsRevisionFilesByDirectoryAndSortsFoldersFirst()
    {
        GitRevisionFile[] files =
        [
            new("README.md", "a", "blob", 12, "100644"),
            new("Source/Game/Player.cpp", "b", "blob", 42, "100644"),
            new("Source/Game/Player.h", "c", "blob", 24, "100644"),
            new("Content/Test.uasset", "d", "blob", 1024, "100644")
        ];

        IReadOnlyList<GitRevisionTreeNode> roots = GitRevisionTreeNode.Build(files);

        Assert.Equal(["Content", "Source", "README.md"], roots.Select(node => node.Name));
        GitRevisionTreeNode source = roots.Single(node => node.Name == "Source");
        GitRevisionTreeNode game = Assert.Single(source.Children);
        Assert.Equal(["Player.cpp", "Player.h"], game.Children.Select(node => node.Name));
        Assert.Equal("2 file(s)", source.Detail);
    }
}
