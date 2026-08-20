using CyRevision.Desktop.Diagnostics;
using CyRevision.Desktop.Workspace;

namespace CyRevision.Core.Tests;

public sealed class RepositoryConsoleServiceTests
{
    [Fact]
    public async Task Console_runs_in_selected_directory_and_persists_scoped_history()
    {
        string root = CreateTemporaryDirectory();
        string other = CreateTemporaryDirectory();
        string config = CreateTemporaryDirectory();
        try
        {
            RepositoryConsoleService service = new(config);
            List<string> output = [];
            string command = OperatingSystem.IsWindows()
                ? "[System.IO.Path]::GetFileName((Get-Location).Path)"
                : "basename \"$PWD\"";
            string shell = OperatingSystem.IsWindows() ? "PowerShell" : "Bash";

            RepositoryCommandResult result = await service.ExecuteAsync(
                root, command, shell, (line, _) => output.Add(line));

            Assert.True(result.ExitCode == 0,
                $"Console exited with {result.ExitCode}: {string.Join(Environment.NewLine, output)}");
            Assert.Contains(Path.GetFileName(root), string.Join(Environment.NewLine, output));
            Assert.Single(service.GetHistory(root));
            Assert.Empty(service.GetHistory(other));
            Assert.Single(new RepositoryConsoleService(config).GetHistory(root));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(other, true);
            Directory.Delete(config, true);
        }
    }

    [Fact]
    public void Application_log_is_reloaded_from_disk()
    {
        string data = CreateTemporaryDirectory();
        try
        {
            string logPath;
            using (ApplicationLogService writer = new(data))
            {
                writer.Warning("Test", "Persistent diagnostic message");
                logPath = writer.CurrentLogPath;
            }

            Assert.True(File.Exists(logPath));
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(logPath)));

            using ApplicationLogService reader = new(data);
            ApplicationLogEntry entry = Assert.Single(reader.LoadRecent());
            Assert.Equal(ApplicationLogLevel.Warning, entry.Level);
            Assert.Equal("Test", entry.Area);
            Assert.Equal("Persistent diagnostic message", entry.Message);
        }
        finally
        {
            Directory.Delete(data, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        // Keep subprocess-based console tests under the test output directory. Some managed
        // sandboxes intentionally deny a child shell access to the user TEMP directory even
        // though the test host itself can create files there.
        string path = Path.Combine(AppContext.BaseDirectory, "cyrevision-console-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
