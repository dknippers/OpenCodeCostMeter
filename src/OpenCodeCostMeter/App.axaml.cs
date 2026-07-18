using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenCodeCostMeter.Data;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.Services;
using OpenCodeCostMeter.ViewModels;

namespace OpenCodeCostMeter;

public sealed partial class App : Application
{
    private Settings _settings = new();
    private SettingsStore _store = null!;
    private UsagePoller _poller = null!;
    private MainWindowViewModel _vm = null!;
    private MainWindow _window = null!;
    private TrayIconService? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _store = new SettingsStore(SettingsStore.DefaultPath());
            _settings = _store.Load();

            var dbPath = DbLocator.ResolveDatabasePath(Program.Options.DbPath);
            if (dbPath == null)
            {
                ShowDatabaseNotFound(desktop, Program.Options.DbPath);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var repo = new MessageTableRepository(dbPath);
            _poller = new UsagePoller(repo, _settings.PollIntervalSeconds, new AvaloniaUiTimer());
            _vm = new MainWindowViewModel(_poller, new AvaloniaUiTimer()) { IsExpanded = _settings.IsExpanded };

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
                desktop.Shutdown();
            });

            _vm.Loaded += OnLoaded;
            _vm.Error += OnError;
            _vm.Start();

            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyWindowPositionAndSettings()
    {
        if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y))
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.SetPositionDips(_settings.X, _settings.Y);
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

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            _store.Save(_settings);
            _vm.Dispose();
            _poller.Dispose();
            _trayIcon?.Dispose();
        }
        catch
        {
            // best-effort
        }
    }

    private static void ShowDatabaseNotFound(IClassicDesktopStyleApplicationLifetime desktop, string? commandLinePath)
    {
        var message = !string.IsNullOrWhiteSpace(commandLinePath)
            ? $"Database not found: {commandLinePath}"
            : $"Could not find the opencode database at{Environment.NewLine}{DbLocator.DefaultPath()}{Environment.NewLine}{Environment.NewLine}Use --db-path <path> to specify an alternative location.";

        var dialog = new DatabaseNotFoundWindow(message);
        dialog.Closed += (_, _) => desktop.Shutdown(1);
        dialog.Show();
    }
}
