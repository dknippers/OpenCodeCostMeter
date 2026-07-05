using System.Windows;
using System.Windows.Input;
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

    private void ApplySettingsToVisuals()
    {
        Topmost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.Opacity, 0.4, 1.0);
    }

    private void OnSettingsChanged()
    {
        ApplySettingsToVisuals();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ForceNow();
    }

    private void OnCardMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = BuildMenu();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        ToggleButton.Visibility = Visibility.Visible;
    }

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        ToggleButton.Visibility = Visibility.Collapsed;
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

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
        foreach (var s in new[] { 5.0, 10.0, 30.0, 60.0 })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"{s} seconds",
                IsCheckable = true,
                IsChecked = Math.Abs(_settings.PollIntervalSeconds - s) < 0.01
            };
            var s2 = s;
            item.Click += (_, _) =>
            {
                _settings.PollIntervalSeconds = s2;
                ViewModel.SetInterval(s2);
                OnSettingsChanged();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        menu.Items.Add(MakeHeader("Opacity"));
        foreach (var o in new[] { 0.20, 0.40, 0.60, 0.80, 1.00 })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"{(int)(o * 100)}%",
                IsCheckable = true,
                IsChecked = Math.Abs(_settings.Opacity - o) < 0.001
            };
            var o2 = o;
            item.Click += (_, _) =>
            {
                _settings.Opacity = o2;
                Opacity = Math.Clamp(o2, 0.4, 1.0);
                OnSettingsChanged();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

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
}