using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCodeCostMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(500);

    private WidgetSettings _settings = new();
    private readonly DispatcherTimer _saveDebounce;

    private System.Windows.Point _dragStartPosition;
    private DateTime _dragStartTime;
    private bool _isDragging;

    public bool IsExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        var border = (System.Windows.Controls.Border)Content;
        border.MouseMove += OnCardMouseMove;
        border.MouseLeftButtonUp += OnCardMouseLeftButtonUp;
        SizeChanged += OnSizeChanged;

        _saveDebounce = new DispatcherTimer { Interval = SaveDebounceDelay };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
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
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _dragStartPosition = e.GetPosition(this);
            _dragStartTime = DateTime.Now;
            _isDragging = false;

            var border = (System.Windows.Controls.Border)sender;
            border.CaptureMouse();

            e.Handled = true;
        }
    }

    private void OnCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        var offset = pos - _dragStartPosition;
        var distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);

        if (distance > 4)
        {
            _isDragging = true;
            var border = (System.Windows.Controls.Border)sender;
            border.ReleaseMouseCapture();
            DragMove();
        }
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var border = (System.Windows.Controls.Border)sender;

        if (!_isDragging)
        {
            var duration = DateTime.Now - _dragStartTime;
            if (duration < TimeSpan.FromMilliseconds(400))
            {
                ViewModel.ToggleBreakdownCommand.Execute(null);
            }
        }

        _isDragging = false;
        border.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width == 0 || e.PreviousSize.Height == 0)
            return;

        var dw = e.NewSize.Width - e.PreviousSize.Width;
        var dh = e.NewSize.Height - e.PreviousSize.Height;
        if (dw == 0 && dh == 0)
            return;

        var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var screenCenter = new System.Windows.Point(
            screen.WorkingArea.X + screen.WorkingArea.Width / 2.0,
            screen.WorkingArea.Y + screen.WorkingArea.Height / 2.0);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var toDevice = source.CompositionTarget.TransformToDevice;
        var windowCenterDevice = toDevice.Transform(
            new System.Windows.Point(Left + ActualWidth / 2, Top + ActualHeight / 2));

        if (windowCenterDevice.X >= screenCenter.X)
            Left -= dw;
        if (windowCenterDevice.Y >= screenCenter.Y)
            Top -= dh;
    }

    private void OnCardMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = BuildMenu();
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
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