using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenCodeCostMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(100);

    private WidgetSettings _settings = new();
    private readonly DispatcherTimer _hoverTimer;
    private bool _hoverShowPending;

    public bool IsExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = HoverDelay };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_hoverShowPending)
                ToggleButton.Visibility = Visibility.Visible;
            else
                ToggleButton.Visibility = Visibility.Collapsed;
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!IsExitRequested)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
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
        Opacity = Math.Clamp(_settings.Opacity, 0.05, 1.0);
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
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnCardMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverShowPending = true;
        _hoverTimer.Start();
    }

    private void OnCardMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverShowPending = false;
        _hoverTimer.Start();
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var itemStyle = (System.Windows.Style)FindResource("CardMenuItem");
        var menu = new System.Windows.Controls.ContextMenu();

        var onTop = new System.Windows.Controls.MenuItem
        {
            Header = "Always on top",
            IsCheckable = true,
            IsChecked = _settings.AlwaysOnTop,
            Style = itemStyle
        };
        onTop.Click += (_, _) =>
        {
            _settings.AlwaysOnTop = onTop.IsChecked;
            Topmost = _settings.AlwaysOnTop;
            OnSettingsChanged();
        };
        menu.Items.Add(onTop);

        menu.Items.Add(MakeHeader("Poll interval", itemStyle));
        var pollSliderItem = new System.Windows.Controls.MenuItem { IsHitTestVisible = true, Style = itemStyle };
        var pollPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var pollSlider = new System.Windows.Controls.Slider
        {
            Minimum = 5,
            Maximum = 60,
            Value = _settings.PollIntervalSeconds,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 120,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        var pollLabel = new System.Windows.Controls.TextBlock
        {
            Text = $"{(int)_settings.PollIntervalSeconds}s",
            TextAlignment = System.Windows.TextAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(10, 0, 10, 0),
            Width = 32
        };
        pollSlider.ValueChanged += (_, e) =>
        {
            _settings.PollIntervalSeconds = e.NewValue;
            ViewModel.SetInterval(e.NewValue);
            pollLabel.Text = $"{(int)e.NewValue}s";
            OnSettingsChanged();
        };
        pollPanel.Children.Add(pollSlider);
        pollPanel.Children.Add(pollLabel);
        pollSliderItem.Header = pollPanel;
        menu.Items.Add(pollSliderItem);

        menu.Items.Add(MakeHeader("Opacity", itemStyle));
        var opacitySliderItem = new System.Windows.Controls.MenuItem { IsHitTestVisible = true, Style = itemStyle };
        var opacityPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var opacitySlider = new System.Windows.Controls.Slider
        {
            Minimum = 5,
            Maximum = 100,
            Value = _settings.Opacity * 100,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 120,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        var opacityLabel = new System.Windows.Controls.TextBlock
        {
            Text = $"{(int)(_settings.Opacity * 100)}%",
            TextAlignment = System.Windows.TextAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(10, 0, 10, 0),
            Width = 32
        };
        opacitySlider.ValueChanged += (_, e) =>
        {
            var val = e.NewValue / 100.0;
            _settings.Opacity = val;
            Opacity = val;
            opacityLabel.Text = $"{(int)e.NewValue}%";
            OnSettingsChanged();
        };
        opacityPanel.Children.Add(opacitySlider);
        opacityPanel.Children.Add(opacityLabel);
        opacitySliderItem.Header = opacityPanel;
        menu.Items.Add(opacitySliderItem);

        var hideItem = new System.Windows.Controls.MenuItem { Header = "Hide", Style = itemStyle };
        hideItem.Click += (_, _) => Hide();
        menu.Items.Add(hideItem);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit", Style = itemStyle };
        exitItem.Click += (_, _) =>
        {
            IsExitRequested = true;
            System.Windows.Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

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

    private System.Windows.Controls.MenuItem MakeHeader(string text, System.Windows.Style style)
    {
        var item = MakeHeader(text);
        item.Style = style;
        return item;
    }
}