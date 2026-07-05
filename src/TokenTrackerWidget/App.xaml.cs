using System.IO;
using System.Windows;
using TokenTrackerWidget.Data;
using TokenTrackerWidget.Models;
using TokenTrackerWidget.Services;
using TokenTrackerWidget.ViewModels;

namespace TokenTrackerWidget;

public partial class App : Application
{
    private WidgetSettings _settings = new();
    private SettingsStore _store = null!;
    private UsagePoller _poller = null!;
    private WidgetViewModel _vm = null!;
    private MainWindow _window = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--dump", StringComparison.OrdinalIgnoreCase))
        {
            DumpTodayToConsole();
            Shutdown(0);
            return;
        }

        _store = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _store.Load();

        if (!DbLocator.TryResolveDatabasePath(_settings, out var dbPath, out _))
        {
            var pick = PromptForDatabase(_settings);
            if (pick == null)
            {
                Shutdown(0);
                return;
            }
            dbPath = pick;
            _settings.DatabasePathOverride = pick;
        }

        var repo = new MessageTableRepository(dbPath);
        _poller = new UsagePoller(repo, _settings.PollIntervalSeconds);
        _vm = new WidgetViewModel(_poller)
        {
            DisplayMode = _settings.DisplayMode
        };

        _window = new MainWindow
        {
            DataContext = _vm,
            Settings = _settings
        };
        _window.SettingsChanged += OnSettingsChanged;
        _window.Closed += OnWindowClosed;

        ShowWindow();
        _vm.Start();
    }

    private void ShowWindow()
    {
        if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y))
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Left = _settings.X;
            _window.Top = _settings.Y;
        }
        if (_settings.Width > 0) _window.Width = _settings.Width;
        if (_settings.Height > 0) _window.Height = _settings.Height;
        _window.Topmost = _settings.AlwaysOnTop;
        _window.Opacity = Math.Clamp(_settings.Opacity, 0.4, 1.0);
        _window.Show();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _settings = ((MainWindow)sender!).Settings;
        _vm.DisplayMode = _settings.DisplayMode;
        _poller.SetInterval(_settings.PollIntervalSeconds);
        _window.Topmost = _settings.AlwaysOnTop;
        _window.Opacity = Math.Clamp(_settings.Opacity, 0.4, 1.0);
        StartupHelper.SetRunAtStartup(_settings.RunAtStartup);
        _store.Save(_settings);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            _settings.X = _window.Left;
            _settings.Y = _window.Top;
            _settings.Width = _window.ActualWidth;
            _settings.Height = _window.ActualHeight;
            _settings.DisplayMode = _vm.DisplayMode;
            _store.Save(_settings);
            _vm.Dispose();
            _poller.Dispose();
        }
        catch
        {
            // best-effort
        }
    }

    private static void DumpTodayToConsole()
    {
        try
        {
            var dbPath = DbLocator.DefaultPath();
            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"db not found: {dbPath}");
                return;
            }
            var repo = new MessageTableRepository(dbPath);
            var start = Services.UsagePoller.StartOfTodayMs();
            var snap = repo.GetToday(start);
            Console.WriteLine($"day        {snap.DayKey}");
            Console.WriteLine($"active     {snap.ActiveModel ?? "(none)"}");
            Console.WriteLine($"live       {snap.IsLive}");
            Console.WriteLine($"calls      {snap.Calls}");
            Console.WriteLine($"input      {snap.Input}");
            Console.WriteLine($"output     {snap.Output}");
            Console.WriteLine($"reasoning  {snap.Reasoning}");
            Console.WriteLine($"cache_r    {snap.CacheRead}");
            Console.WriteLine($"cache_w    {snap.CacheWrite}");
            Console.WriteLine($"cost_usd   {snap.Cost:F6}");
            Console.WriteLine($"models:");
            foreach (var b in snap.Models)
            {
                Console.WriteLine($"  {b.Provider}/{b.Model} calls={b.Calls} in={b.Input} out={b.Output} cache_r={b.CacheRead} cost=${b.Cost:F6}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    private string? PromptForDatabase(WidgetSettings settings)
    {
        var msg = "Could not find the opencode database at the default location\n" +
                  $"{DbLocator.DefaultPath()}\n\nBrowse to opencode.db?";
        var result = MessageBox.Show(msg, "OpenCode Token Tracker",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return null;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select opencode.db",
            Filter = "SQLite DB (opencode.db)|opencode.db|All files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return null;
        return dlg.FileName;
    }
}