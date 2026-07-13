using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenCodeCostMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(500);

    private WidgetSettings _settings = new();
    private readonly DispatcherTimer _saveDebounce;

    private System.Windows.Point? _dragStartPosition;
    private bool _isDragging;
    private ResizeAnchorFlags? _previousResizeAnchor;

    [Flags]
    private enum ResizeAnchorFlags
    {
        None = 0,
        SpansX = 1,
        SpansY = 2,
        TopLeftQuadrant = 4,
        TopRightQuadrant = 8,
        BottomRightQuadrant = 16,
        BottomLeftQuadrant = 32
    }

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

        Loaded += (_, _) => ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closing += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsExpanded))
        {
            _settings.IsExpanded = ViewModel.IsExpanded;
            OnSettingsChanged();
        }
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

            var border = (System.Windows.Controls.Border)sender;
            border.CaptureMouse();

            e.Handled = true;
        }
    }

    private void OnCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging ||
            e.LeftButton != MouseButtonState.Pressed ||
            _dragStartPosition is null)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var dx = Math.Abs(pos.X - _dragStartPosition.Value.X);
        var dy = Math.Abs(pos.Y - _dragStartPosition.Value.Y);

        if (dx > SystemParameters.MinimumHorizontalDragDistance ||
            dy > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            var border = (System.Windows.Controls.Border)sender;
            border.ReleaseMouseCapture();
            DragMove();
            _isDragging = false;
            _dragStartPosition = null;
            _previousResizeAnchor = null;
            SnapToEdgeIfOutOfBounds();
        }
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            ViewModel.ToggleExpandCommand.Execute(null);
        }

        _isDragging = false;
        _dragStartPosition = null;

        var border = (System.Windows.Controls.Border)sender;
        border.ReleaseMouseCapture();
        e.Handled = true;
    }

    internal void SnapToEdgeIfOutOfBounds()
    {
        var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var toDIP = source.CompositionTarget.TransformFromDevice;
        var boundsTopLeft = toDIP.Transform(new System.Windows.Point(screen.WorkingArea.X, screen.WorkingArea.Y));
        var boundsBottomRight = toDIP.Transform(new System.Windows.Point(
            screen.WorkingArea.X + screen.WorkingArea.Width,
            screen.WorkingArea.Y + screen.WorkingArea.Height));
        var bounds = new Rect(boundsTopLeft, boundsBottomRight);

        double left = Left;
        double top = Top;
        bool changed = false;

        if (left < bounds.Left)
        {
            left = bounds.Left;
            changed = true;
        }
        else if (left + ActualWidth > bounds.Right)
        {
            left = bounds.Right - ActualWidth;
            if (left < bounds.Left)
                left = bounds.Left;
            changed = true;
        }

        if (top < bounds.Top)
        {
            top = bounds.Top;
            changed = true;
        }
        else if (top + ActualHeight > bounds.Bottom)
        {
            top = bounds.Bottom - ActualHeight;
            if (top < bounds.Top)
                top = bounds.Top;
            changed = true;
        }

        if (changed)
        {
            Left = left;
            Top = top;
        }
    }

    private void CenterHorizontally()
    {
        var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var toDIP = source.CompositionTarget.TransformFromDevice;
        var boundsTopLeft = toDIP.Transform(new System.Windows.Point(screen.WorkingArea.X, screen.WorkingArea.Y));
        var boundsBottomRight = toDIP.Transform(new System.Windows.Point(
            screen.WorkingArea.X + screen.WorkingArea.Width,
            screen.WorkingArea.Y + screen.WorkingArea.Height));
        var bounds = new Rect(boundsTopLeft, boundsBottomRight);

        Left = bounds.X + (bounds.Width - ActualWidth) / 2;
    }

    private void CenterVertically()
    {
        var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var toDIP = source.CompositionTarget.TransformFromDevice;
        var boundsTopLeft = toDIP.Transform(new System.Windows.Point(screen.WorkingArea.X, screen.WorkingArea.Y));
        var boundsBottomRight = toDIP.Transform(new System.Windows.Point(
            screen.WorkingArea.X + screen.WorkingArea.Width,
            screen.WorkingArea.Y + screen.WorkingArea.Height));
        var bounds = new Rect(boundsTopLeft, boundsBottomRight);

        Top = bounds.Y + (bounds.Height - ActualHeight) / 2;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width == 0 || e.PreviousSize.Height == 0)
            return;

        // Use previous dimensions because ActualWidth/Height already reflect the new size.
        var width = e.PreviousSize.Width;
        var height = e.PreviousSize.Height;

        var dw = e.NewSize.Width - width;
        var dh = e.NewSize.Height - height;
        if (dw == 0 && dh == 0)
            return;

        var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var screenCenterDevice = new System.Windows.Point(
            screen.WorkingArea.X + screen.WorkingArea.Width / 2.0,
            screen.WorkingArea.Y + screen.WorkingArea.Height / 2.0);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var toDIP = source.CompositionTarget.TransformFromDevice;
        var screenCenterDIP = toDIP.Transform(screenCenterDevice);

        ResizeAnchorFlags flags;

        if (_previousResizeAnchor.HasValue)
        {
            flags = _previousResizeAnchor.Value;
            _previousResizeAnchor = null;
        }
        else
        {
            flags = ComputeResizeAnchorFlags(Left, Top, width, height, screenCenterDIP);
            _previousResizeAnchor = flags;
        }

        if (flags.HasFlag(ResizeAnchorFlags.SpansX))
            Left -= dw / 2;
        else if (flags.HasFlag(ResizeAnchorFlags.TopRightQuadrant) ||
                 flags.HasFlag(ResizeAnchorFlags.BottomRightQuadrant))
            Left -= dw;

        if (flags.HasFlag(ResizeAnchorFlags.SpansY))
            Top -= dh / 2;
        else if (flags.HasFlag(ResizeAnchorFlags.BottomLeftQuadrant) ||
                 flags.HasFlag(ResizeAnchorFlags.BottomRightQuadrant))
            Top -= dh;
    }

    private static ResizeAnchorFlags ComputeResizeAnchorFlags(
        double left, double top, double width, double height, System.Windows.Point screenCenter)
    {
        var flags = ResizeAnchorFlags.None;

        bool spansCenterX = left <= screenCenter.X && (left + width) >= screenCenter.X;
        bool spansCenterY = top <= screenCenter.Y && (top + height) >= screenCenter.Y;

        if (spansCenterX)
            flags |= ResizeAnchorFlags.SpansX;
        if (spansCenterY)
            flags |= ResizeAnchorFlags.SpansY;

        bool inLeft = left < screenCenter.X;
        bool inRight = (left + width) > screenCenter.X;
        bool inTop = top < screenCenter.Y;
        bool inBottom = (top + height) > screenCenter.Y;

        if (inLeft && inTop)
            flags |= ResizeAnchorFlags.TopLeftQuadrant;
        if (inRight && inTop)
            flags |= ResizeAnchorFlags.TopRightQuadrant;
        if (inRight && inBottom)
            flags |= ResizeAnchorFlags.BottomRightQuadrant;
        if (inLeft && inBottom)
            flags |= ResizeAnchorFlags.BottomLeftQuadrant;

        return flags;
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

        var centerHorizItem = new System.Windows.Controls.MenuItem { Header = "Center horizontally", Style = itemStyle };
        centerHorizItem.Click += (_, _) =>
        {
            _previousResizeAnchor = null;
            CenterHorizontally();
        };
        menu.Items.Add(centerHorizItem);

        var centerVertItem = new System.Windows.Controls.MenuItem { Header = "Center vertically", Style = itemStyle };
        centerVertItem.Click += (_, _) =>
        {
            _previousResizeAnchor = null;
            CenterVertically();
        };
        menu.Items.Add(centerVertItem);

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