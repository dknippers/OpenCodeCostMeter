using Avalonia;
using Fonts.Avalonia.CascadiaCode;
using OpenCodeCostMeter.Platform;

namespace OpenCodeCostMeter;

internal static class Program
{
    public static LaunchOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = LaunchOptions.Parse(args);

        if (Options.ShowHelp)
        {
            ConsoleHelper.PrintHelp();
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithCascadiaCodeFont()
            .LogToTrace();
}
