using System.Reflection;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed class StartupRegistrationService
{
    private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApplicationName = "CyRevision";
    private const string MacLabel = "com.cyrevision.desktop";
    private readonly StartupLaunchTarget _target;

    public StartupRegistrationService()
    {
        _target = StartupLaunchTarget.ForCurrentProcess();
    }

    public void SetEnabled(bool enabled, bool startHidden)
    {
        if (OperatingSystem.IsWindows())
        {
            SetWindowsRegistration(enabled, startHidden);
            return;
        }

        string path = GetRegistrationPath();
        if (!enabled)
        {
            if (File.Exists(path))
            {
                RejectLinkTarget(path);
                File.Delete(path);
            }

            return;
        }

        string content = OperatingSystem.IsMacOS()
            ? CreateMacLaunchAgent(startHidden)
            : CreateLinuxDesktopEntry(startHidden);
        WriteRegistrationFile(path, content);
    }

    [SupportedOSPlatform("windows")]
    private void SetWindowsRegistration(bool enabled, bool startHidden)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(WindowsRunKey, writable: true)
                                ?? throw new InvalidOperationException("Unable to open the current-user startup registry key.");
        if (!enabled)
        {
            key.DeleteValue(ApplicationName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(
            ApplicationName,
            _target.WithBackground(startHidden).ToWindowsCommandLine(),
            RegistryValueKind.String);
    }

    private string GetRegistrationPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("The current user profile directory could not be resolved.");
        }

        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "LaunchAgents", MacLabel + ".plist")
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"),
                "autostart",
                "cyrevision.desktop");
    }

    private string CreateLinuxDesktopEntry(bool startHidden)
    {
        StartupLaunchTarget target = _target.WithBackground(startHidden);
        string command = string.Join(' ', new[] { target.ExecutablePath }.Concat(target.Arguments)
            .Select(QuoteDesktopArgument));
        return "[Desktop Entry]\n" +
               "Type=Application\n" +
               "Version=1.0\n" +
               "Name=CyRevision\n" +
               "Comment=CyRevision background services and system tray\n" +
               $"Exec={command}\n" +
               "Terminal=false\n" +
               "X-GNOME-Autostart-enabled=true\n";
    }

    private string CreateMacLaunchAgent(bool startHidden)
    {
        StartupLaunchTarget target = _target.WithBackground(startHidden);
        string arguments = string.Join(
            Environment.NewLine,
            new[] { target.ExecutablePath }.Concat(target.Arguments)
                .Select(argument => $"      <string>{SecurityElement.Escape(argument)}</string>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
               "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
               "<plist version=\"1.0\">\n" +
               "  <dict>\n" +
               "    <key>Label</key>\n" +
               $"    <string>{MacLabel}</string>\n" +
               "    <key>ProgramArguments</key>\n" +
               "    <array>\n" + arguments + "\n    </array>\n" +
               "    <key>RunAtLoad</key><true/>\n" +
               "    <key>ProcessType</key><string>Interactive</string>\n" +
               "  </dict>\n" +
               "</plist>\n";
    }

    private static void WriteRegistrationFile(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidOperationException("The startup registration path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            RejectLinkTarget(path);
        }

        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RejectLinkTarget(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("CyRevision refuses to replace a startup registration symbolic link.");
        }
    }

    private static string QuoteDesktopArgument(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal) + "\"";

    private sealed record StartupLaunchTarget(string ExecutablePath, IReadOnlyList<string> Arguments)
    {
        public static StartupLaunchTarget ForCurrentProcess()
        {
            string executable = Environment.ProcessPath
                                ?? throw new InvalidOperationException("The CyRevision executable path could not be resolved.");
            string entryAssembly = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
            bool usesDotnetHost = Path.GetFileNameWithoutExtension(executable)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            return usesDotnetHost && entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new StartupLaunchTarget(executable, [entryAssembly])
                : new StartupLaunchTarget(executable, []);
        }

        public StartupLaunchTarget WithBackground(bool background)
        {
            List<string> arguments = Arguments
                .Where(argument => !string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (background)
            {
                arguments.Add("--background");
            }

            return this with { Arguments = arguments };
        }

        public string ToWindowsCommandLine() => string.Join(
            ' ',
            new[] { ExecutablePath }.Concat(Arguments).Select(QuoteWindowsArgument));

        private static string QuoteWindowsArgument(string value)
        {
            if (value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character != '"'))
            {
                return value;
            }

            StringBuilder result = new("\"");
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', backslashes);
                    result.Append(character);
                }

                backslashes = 0;
            }

            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
