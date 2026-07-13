using OpenCodeCostMeter.Data;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;
using OpenCodeCostMeter.ViewModels;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace OpenCodeCostMeter;

public partial class App : System.Windows.Application
{
    private WidgetSettings _settings = new();
    private SettingsStore _store = null!;
    private UsagePoller _poller = null!;
    private WidgetViewModel _vm = null!;
    private MainWindow _window = null!;
    private TrayIconService _trayIcon = null!;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = ParseArgs(e.Args);

        if (options.ShowHelp)
        {
            PrintHelp();
            Shutdown(0);
            return;
        }

        _store = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _store.Load();

        var dbPath = DbLocator.ResolveDatabasePath(options.DbPath);
        if (dbPath == null)
        {
            ShowDatabaseNotFound(options.DbPath);
            Shutdown(1);
            return;
        }

        var repo = new MessageTableRepository(dbPath);
        _poller = new UsagePoller(repo, _settings.PollIntervalSeconds);
        _vm = new WidgetViewModel(_poller) { IsExpanded = _settings.IsExpanded };

        _window = new MainWindow
        {
            DataContext = _vm,
            Settings = _settings
        };
        _window.SettingsChanged += OnSettingsChanged;

        ApplyWindowPositionAndSettings();

        _trayIcon = new TrayIconService(_window, "OpenCode Cost Meter", () =>
        {
            _window.IsExitRequested = true;
            Shutdown();
        });

        _vm.Loaded += OnLoaded;
        _vm.Error += OnError;
        _vm.Start();
    }

    private void ApplyWindowPositionAndSettings()
    {
        if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y))
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Left = _settings.X;
            _window.Top = _settings.Y;
        }
        else
        {
            // First launch: center the window on the screen
            _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _window.Topmost = _settings.AlwaysOnTop;
        _window.Opacity = Math.Clamp(_settings.Opacity, 0.05, 1.0);
    }

    private void ShowWindow()
    {
        if (_window.IsVisible)
        {
            // Already visible
            return;
        }

        _window.Show();
        _window.SnapToEdgeIfOutOfBounds();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _vm.Loaded -= OnLoaded;
        ShowWindow();
    }

    private void OnError(object? sender, EventArgs e)
    {
        _vm.Error -= OnError;
        ShowWindow();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _settings = ((MainWindow)sender!).Settings;
        _poller.SetInterval(_settings.PollIntervalSeconds);
        _window.Topmost = _settings.AlwaysOnTop;
        _window.Opacity = Math.Clamp(_settings.Opacity, 0.05, 1.0);
        _store.Save(_settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _settings.X = _window.Left;
            _settings.Y = _window.Top;
            _store.Save(_settings);
            _vm.Dispose();
            _poller.Dispose();
            _trayIcon?.Dispose();
        }
        catch
        {
            // best-effort
        }

        base.OnExit(e);
    }

    private static LaunchOptions ParseArgs(string[] args)
    {
        var options = new LaunchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-?", StringComparison.Ordinal) ||
                string.Equals(arg, "/?", StringComparison.Ordinal))
            {
                options.ShowHelp = true;
            }
            else if (string.Equals(arg, "--db-path", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    options.DbPath = args[++i];
                }
            }
        }
        return options;
    }

    private static void PrintHelp()
    {
        if (!AttachConsole(-1))
        {
            AllocConsole();
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        Console.WriteLine("OpenCode Cost Meter");
        Console.WriteLine();
        Console.WriteLine("Usage: OpenCodeCostMeter.exe [--db-path <path>] [--help]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --db-path <path>  Use an alternative opencode.db location.");
        Console.WriteLine("  --help            Show this help text and exit.");

        FreeConsole();
    }

    private static void ShowDatabaseNotFound(string? commandLinePath)
    {
        var message = !string.IsNullOrWhiteSpace(commandLinePath)
            ? $"Database not found: {commandLinePath}"
            : $"Could not find the opencode database at{Environment.NewLine}{DbLocator.DefaultPath()}{Environment.NewLine}{Environment.NewLine}Use --db-path <path> to specify an alternative location.";

        System.Windows.MessageBox.Show(message, "OpenCode Cost Meter",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed class LaunchOptions
    {
        public string? DbPath { get; set; }
        public bool ShowHelp { get; set; }
    }
}
