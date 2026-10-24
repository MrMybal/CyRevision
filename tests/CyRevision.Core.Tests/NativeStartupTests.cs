using CyRevision.Desktop.Native;
using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Core.Tests;

public sealed class NativeStartupTests
{
    [Fact]
    public void AnIsolatedNativeProfileDoesNotUseMachineStartupRegistration()
    {
        Assert.True(NativeBackendHost.IsIsolatedProfile(["--native-backend", "--profile-dir", "fixture"]));
        Assert.False(NativeBackendHost.IsIsolatedProfile(["--native-backend"]));
        Assert.False(NativeBackendHost.IsIsolatedProfile(["--classic", "--profile-dir", "fixture"]));
        Assert.False(NativeBackendHost.IsIsolatedProfile(null));
    }

    [Fact]
    public void NativeStartupUsesTheFrontendNotTheEngineAndRetainsBackgroundMode()
    {
        string target = Path.Combine(Path.GetTempPath(), "CyRevision Fixture", "CyRevision.Native.exe");
        StartupRegistrationService service = new(target);
        Assert.Equal($"\"{target}\" --background", service.GetWindowsCommandLine(true));
        Assert.Equal($"\"{target}\"", service.GetWindowsCommandLine(false));
        Assert.DoesNotContain("CyRevision.Desktop", service.GetWindowsCommandLine(true));
    }
}
