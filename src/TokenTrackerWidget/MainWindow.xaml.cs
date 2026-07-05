using System.IO;
using System.Windows;
using System.Windows.Input;
using TokenTrackerWidget.Data;
using TokenTrackerWidget.Models;
using TokenTrackerWidget.ViewModels;

namespace TokenTrackerWidget;

public partial class MainWindow : Window
{
    private WidgetSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    public WidgetSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            ApplySettingsToVisuals();
        }
    }

    public event EventHandler? SettingsChanged;
    public WidgetViewModel ViewModel => (WidgetViewModel)DataContext;

    public void NotifyStartupShortcutFailed()
    {
        // best-effort; surfacing only a footer line is enough
        if (DataContext is WidgetViewModel vm)
            vm.LastErrorText = "(startup shortcut failed)";
    }

    private void ApplySettingsToVisuals()
    {
        Topmost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.Opacity, 0.4, 1.0);
        if (DataContext is WidgetViewModel vm)
            vm.DisplayMode = _settings.DisplayMode;
    }

    private void OnSettingsChanged()
    {
        ApplySettingsToVisuals();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void OnMenuClicked(object sender, RoutedEventArgs e)
    {
        var menu = BuildMenu();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Display mode
        menu.Items.Add(MakeHeader("Display mode"));
        foreach (DisplayMode m in Enum.GetValues(typeof(DisplayMode)))
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = m.ToString(),
                IsCheckable = true,
                IsChecked = _settings.DisplayMode == m,
                Tag = m
            };
            item.Click += (_, _) =>
            {
                _settings.DisplayMode = (DisplayMode)item.Tag;
                if (DataContext is WidgetViewModel vm) vm.DisplayMode = _settings.DisplayMode;
                OnSettingsChanged();
                RefreshMenuChecks(menu);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Always on top
        var onTop = new System.Windows.Controls.MenuItem
        {
            Header = "Always on top",
            IsCheckable = true,
            IsChecked = _settings.AlwaysOnTop
        };
        onTop.Click += (_, _) =>
        {
            _settings.AlwaysOnTop = onTop.IsChecked;
            Topmost = _settings.AlwaysOnTop;
            OnSettingsChanged();
        };
        menu.Items.Add(onTop);

        menu.Items.Add(MakeHeader("Poll interval"));
        foreach (var s in new[] { 1.0, 2.5, 5.0, 10.0, 30.0 })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"{s} s",
                IsCheckable = true,
                IsChecked = Math.Abs(_settings.PollIntervalSeconds - s) < 0.01,
                Tag = s
            };
            item.Click += (_, _) =>
            {
                _settings.PollIntervalSeconds = s;
                if (DataContext is WidgetViewModel vm) vm.SetInterval(s);
                OnSettingsChanged();
                RefreshMenuChecks(menu);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        menu.Items.Add(MakeHeader("Opacity"));
        foreach (var o in new[] { 0.60, 0.80, 0.88, 1.00 })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"{(int)(o * 100)}%",
                IsCheckable = true,
                IsChecked = Math.Abs(_settings.Opacity - o) < 0.001,
                Tag = o
            };
            item.Click += (_, _) =>
            {
                _settings.Opacity = o;
                Opacity = Math.Clamp(o, 0.4, 1.0);
                OnSettingsChanged();
                RefreshMenuChecks(menu);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        var runStartup = new System.Windows.Controls.MenuItem
        {
            Header = "Run on Windows startup",
            IsCheckable = true,
            IsChecked = _settings.RunAtStartup
        };
        runStartup.Click += (_, _) =>
        {
            _settings.RunAtStartup = runStartup.IsChecked;
            OnSettingsChanged();
        };
        menu.Items.Add(runStartup);

        var reset = new System.Windows.Controls.MenuItem { Header = "Reset window position" };
        reset.Click += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Width - Width - 16;
            Top = 40;
        };
        menu.Items.Add(reset);

        var sanity = new System.Windows.Controls.MenuItem { Header = "Run sanity check" };
        sanity.Click += (_, _) => RunSanityCheck();
        menu.Items.Add(sanity);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var about = new System.Windows.Controls.MenuItem { Header = "About" };
        about.Click += (_, _) => MessageBox.Show(
            "TokenTrackerWidget\nReads the OpenCode message table\n(today only, local time).",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
        menu.Items.Add(about);

        var quit = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Close();
        menu.Items.Add(quit);

        return menu;
    }

    private static System.Windows.Controls.MenuItem MakeHeader(string text)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = text,
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        return item;
    }

    private static void RefreshMenuChecks(System.Windows.Controls.ContextMenu menu)
    {
        // Toggle checks; visual refresh handled by setting IsChecked via the individual Click handlers
    }

    private void RunSanityCheck()
    {
        try
        {
            var dbPath = _settings.DatabasePathOverride;
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                dbPath = DbLocator.DefaultPath();
            var checker = new TokenTrackerWidget.Data.SessionTableSanityChecker(dbPath);
            var start = Services.UsagePoller.StartOfTodayMs();
            var report = checker.CrossCheckToday(start);
            var msg = $"message vs session (today):\n" +
                      $"  input   msg={report.MessageTableInput}  session={report.SessionTableInput}  Δ={report.MessageTableInput - report.SessionTableInput}\n" +
                      $"  output  msg={report.MessageTableOutput}  session={report.SessionTableOutput}  Δ={report.MessageTableOutput - report.SessionTableOutput}\n" +
                      $"  cache   msg={report.MessageTableCacheRead}  session={report.SessionTableCacheRead}  Δ={report.MessageTableCacheRead - report.SessionTableCacheRead}\n" +
                      $"  cost    msg=${report.MessageTableCostCents / 100.0:F2}  session=${report.SessionTableCostCents / 100.0:F2}  Δ=${(report.MessageTableCostCents - report.SessionTableCostCents) / 100.0:F2}\n" +
                      "(session rows are cumulative across the session lifetime,\nso non-zero deltas are expected when a session spans days.)";
            MessageBox.Show(msg, "Sanity check", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sanity check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}