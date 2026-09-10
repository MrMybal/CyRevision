using CyRevision.Core.Updates;
using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Core.Tests;

public sealed class ApplicationUpdateLauncherTests
{
    [Fact]
    public void CreatePlan_WindowsPassesPathsAsSeparateArguments()
    {
        string packagePath = Path.Combine(Path.GetTempPath(), "Update Cache", "CyRevision Setup.exe");
        string scriptPath = Path.Combine(Path.GetTempPath(), "CyRevision update.ps1");

        ApplicationUpdateLaunchPlan plan = ApplicationUpdateLauncher.CreatePlan(
            packagePath,
            4242,
            UpdatePlatform.Windows,
            scriptPath);

        Assert.Equal(scriptPath, plan.ScriptPath);
        Assert.Contains(packagePath, plan.Arguments);
        Assert.Contains("4242", plan.Arguments);
        Assert.Contains("Wait-Process", plan.ScriptContents, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath $PackagePath", plan.ScriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain(packagePath, plan.ScriptContents, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UpdatePlatform.Linux, "xdg-open")]
    [InlineData(UpdatePlatform.MacOs, "open")]
    public void CreatePlan_UnixWaitsThenOpensPackage(UpdatePlatform platform, string openCommand)
    {
        string packagePath = Path.Combine(Path.GetTempPath(), "CyRevision update package");
        string scriptPath = Path.Combine(Path.GetTempPath(), "cyrevision-update.sh");

        ApplicationUpdateLaunchPlan plan = ApplicationUpdateLauncher.CreatePlan(
            packagePath,
            99,
            platform,
            scriptPath);

        Assert.Equal("/bin/sh", plan.ExecutablePath);
        Assert.Equal(new[] { scriptPath, "99", packagePath }, plan.Arguments);
        Assert.Contains("kill -0", plan.ScriptContents, StringComparison.Ordinal);
        Assert.Contains($"{openCommand} \"$package_path\"", plan.ScriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain(packagePath, plan.ScriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlan_RejectsUnsupportedPlatforms()
    {
        Assert.Throws<PlatformNotSupportedException>(() => ApplicationUpdateLauncher.CreatePlan(
            Path.Combine(Path.GetTempPath(), "CyRevision.package"),
            10,
            UpdatePlatform.Unsupported));
    }
}
