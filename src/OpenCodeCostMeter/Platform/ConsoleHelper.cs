using System.Runtime.InteropServices;

namespace OpenCodeCostMeter.Platform;

/// <summary>
/// Console output helper for --help. The P/Invokes are Windows-only; every
/// call site is guarded so nothing Win32-specific is touched on other
/// platforms (where stdout is connected to the launching terminal anyway).
/// </summary>
public static class ConsoleHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static void PrintHelp()
    {
        var windows = OperatingSystem.IsWindows();

        if (windows)
        {
            if (!AttachConsole(-1))
            {
                AllocConsole();
            }

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }

        Console.WriteLine("OpenCode Cost Meter");
        Console.WriteLine();
        Console.WriteLine("Usage: OpenCodeCostMeter.exe [--db-path <path>] [--help]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --db-path <path>  Use an alternative opencode.db location.");
        Console.WriteLine("  --help            Show this help text and exit.");

        if (windows)
        {
            FreeConsole();
        }
    }
}
