using System.Diagnostics;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitIgnoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-gitignore-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParseRules_DescribesNegationsDirectoriesAndWarnings()
    {
        IReadOnlyList<GitIgnoreRule> rules = GitIgnoreService.ParseRules(
            "# generated\nSaved/\n!Saved/keep.txt\n[invalid\n");

        Assert.Equal("Comment", rules[0].Kind);
        Assert.Equal("Directory", rules[1].Kind);
        Assert.Equal("Include", rules[2].Kind);
        Assert.Contains("Unbalanced", rules[3].Warning);
    }

    [Fact]
    public async Task SaveTestAndList_UseNativeGitSemantics()
    {
        Directory.CreateDirectory(_root);
        await GitAsync("init", "--quiet");
        GitIgnoreService service = new();
        await service.SaveAsync(_root, GitIgnoreSource.Repository, "*.tmp\nGenerated/*\n!Generated/keep.txt\n");
        Directory.CreateDirectory(Path.Combine(_root, "Generated"));
        await File.WriteAllTextAsync(Path.Combine(_root, "drop.tmp"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(_root, "Generated", "drop.txt"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(_root, "Generated", "keep.txt"), "visible");

        GitIgnoreMatch ignored = await service.TestPathAsync(_root, "drop.tmp");
        GitIgnoreMatch included = await service.TestPathAsync(_root, "Generated/keep.txt");
        IReadOnlyList<string> ignoredFiles = await service.ListIgnoredFilesAsync(_root);

        Assert.True(ignored.IsIgnored);
        Assert.Contains("*.tmp", ignored.Pattern);
        Assert.False(included.IsIgnored);
        Assert.Contains("drop.tmp", ignoredFiles);
        Assert.DoesNotContain("Generated/keep.txt", ignoredFiles);
    }

    [Fact]
    public async Task LocalExclude_DoesNotModifyRepositoryGitIgnore()
    {
        Directory.CreateDirectory(_root);
        await GitAsync("init", "--quiet");
        GitIgnoreService service = new();
        await service.SaveAsync(_root, GitIgnoreSource.LocalExclude, ".private/\n");

        GitIgnoreDocument local = await service.LoadAsync(_root, GitIgnoreSource.LocalExclude);

        Assert.Contains(".private/", local.Content);
        Assert.False(File.Exists(Path.Combine(_root, ".gitignore")));
    }

    private async Task GitAsync(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
