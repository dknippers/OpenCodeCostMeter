using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TokenTrackerWidget;

public static class StartupHelper
{
    private static string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "Startup",
            "TokenTrackerWidget.lnk");

    public static void SetRunAtStartup(bool enabled)
    {
        try
        {
            var path = ShortcutPath;
            if (!enabled)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            if (File.Exists(path))
            {
                var existing = ResolveShortcutTarget(path);
                if (string.Equals(existing, exePath, StringComparison.OrdinalIgnoreCase))
                    return;
                File.Delete(path);
            }

            CreateShortcut(path, exePath, "Token Tracker Widget", AppContext.BaseDirectory);
        }
        catch
        {
            // best-effort; do not crash the widget
            if (Application.Current.MainWindow is MainWindow mw)
                mw.NotifyStartupShortcutFailed();
        }
    }

    private static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(
                Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            return (string)sc.TargetPath;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateShortcut(string lnkPath, string targetPath, string description, string workingDir)
    {
        dynamic shell = Activator.CreateInstance(
            Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = targetPath;
        sc.WorkingDirectory = workingDir;
        sc.Description = description;
        sc.WindowStyle = 7; // Minimized (we are a topmost widget, runs minimized anyway)
        sc.Save();
    }
}