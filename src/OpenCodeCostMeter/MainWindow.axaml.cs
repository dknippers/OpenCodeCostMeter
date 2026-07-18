using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenCodeCostMeter.Models;
using OpenCodeCostMeter.ViewModels;
using System.ComponentModel;

namespace OpenCodeCostMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(500);
    private const double DragThreshold = 4;

    private Settings _settings = new();
    private readonly DispatcherTimer _saveDebounce;
    private bool _skipInitialSizeChange = true;

    private Point? _dragStartPosition;
    private PointerPressedEventArgs? _pressedArgs;
    private bool _isDragging;
    private ResizeAnchorFlags? _previousResizeAnchor;
    private bool _syncingFlyout;

    private readonly IBrush _onSurface;
    private readonly IBrush _onSurfaceVariant;
    private readonly FontFamily _uiFont;

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

        _onSurface = (IBrush)Resources["OnSurface"]!;
        _onSurfaceVariant = (IBrush)Resources["OnSurfaceVariant"]!;
        _uiFont = (FontFamily)Resources["UiFont"]!;

        CardBorder.PointerPressed += OnCardPointerPressed;
        CardBorder.PointerMoved += OnCardPointerMoved;
        CardBorder.PointerReleased += OnCardPointerReleased;

        SizeChanged += OnSizeChanged;

        _saveDebounce = new DispatcherTimer { Interval = SaveDebounceDelay };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };

        Closing += OnWindowClosing;
        KeyDown += OnKeyDown;

        CardBorder.ContextFlyout = BuildFlyout();
    }

    public Settings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            ApplySettingsToVisuals();
        }
    }

    public event EventHandler? SettingsChanged;
    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

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

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!IsExitRequested)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            var (x, y) = GetPositionDips();
            _settings.X = x;
            _settings.Y = y;
        }
    }

    // ------------------------------------------------------------------
    // Position helpers. Avalonia's Window.Position is in physical pixels,
    // sizes are in DIPs; settings persist DIPs (same semantics as WPF).

    private double ScalingSafe
    {
        get
        {
            var s = RenderScaling;
            return double.IsNaN(s) || s <= 0 ? 1 : s;
        }
    }

    public void SetPositionDips(double x, double y)
    {
        var s = ScalingSafe;
        Position = new PixelPoint((int)Math.Round(x * s), (int)Math.Round(y * s));
    }

    public (double X, double Y) GetPositionDips()
    {
        var s = ScalingSafe;
        return (Position.X / s, Position.Y / s);
    }

    private Rect? GetWorkingAreaDips()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return null;

        var s = screen.Scaling;
        if (double.IsNaN(s) || s <= 0) s = 1;

        var wa = screen.WorkingArea;
        return new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
    }

    internal void SnapToEdgeIfOutOfBounds()
    {
        var bounds = GetWorkingAreaDips();
        if (bounds is null) return;
        var b = bounds.Value;

        var (left, top) = GetPositionDips();
        var width = Bounds.Width;
        var height = Bounds.Height;
        var changed = false;

        if (left < b.Left)
        {
            left = b.Left;
            changed = true;
        }
        else if (left + width > b.Right)
        {
            left = Math.Max(b.Left, b.Right - width);
            changed = true;
        }

        if (top < b.Top)
        {
            top = b.Top;
            changed = true;
        }
        else if (top + height > b.Bottom)
        {
            top = Math.Max(b.Top, b.Bottom - height);
            changed = true;
        }

        if (changed)
        {
            SetPositionDips(left, top);
        }
    }

    private void CenterHorizontally()
    {
        var bounds = GetWorkingAreaDips();
        if (bounds is null) return;

        var (_, top) = GetPositionDips();
        var left = bounds.Value.X + (bounds.Value.Width - Bounds.Width) / 2;
        SetPositionDips(left, top);
    }

    private void CenterVertically()
    {
        var bounds = GetWorkingAreaDips();
        if (bounds is null) return;

        var (left, _) = GetPositionDips();
        var top = bounds.Value.Y + (bounds.Value.Height - Bounds.Height) / 2;
        SetPositionDips(left, top);
    }

    // ------------------------------------------------------------------
    // Drag to move vs. click to expand/collapse.

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        _dragStartPosition = point.Position;
        _pressedArgs = e;
        e.Pointer.Capture(CardBorder);
        e.Handled = true;
    }

    private void OnCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging || _dragStartPosition is null || _pressedArgs is null) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var dx = Math.Abs(point.Position.X - _dragStartPosition.Value.X);
        var dy = Math.Abs(point.Position.Y - _dragStartPosition.Value.Y);
        if (dx <= DragThreshold && dy <= DragThreshold) return;

        _isDragging = true;
        e.Pointer.Capture(null);
        BeginMoveDrag(_pressedArgs);
        _isDragging = false;
        _dragStartPosition = null;
        _pressedArgs = null;
        _previousResizeAnchor = null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        SnapToEdgeIfOutOfBounds();
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        if (!_isDragging && !e.Handled)
        {
            ViewModel.ToggleExpandCommand.Execute(null);
        }

        _isDragging = false;
        _dragStartPosition = null;
        _pressedArgs = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Quadrant-based resize anchoring (expand/collapse grows "inward").

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width == 0 || e.PreviousSize.Height == 0)
            return;

        if (_skipInitialSizeChange)
        {
            // Skip the second of the two startup SizeChanged events: the HWND is created with
            // CW_USEDEFAULT, so Windows assigns an arbitrary default size, Avalonia arranges at
            // that size first (event 1, filtered by the PreviousSize == 0 guard above), then
            // snaps to the real SizeToContent size (event 2, filtered here). Compensating for
            // this event would move the window away from its restored position.
            _skipInitialSizeChange = false;
            return;
        }

        var width = e.PreviousSize.Width;
        var height = e.PreviousSize.Height;

        var dw = e.NewSize.Width - width;
        var dh = e.NewSize.Height - height;
        if (dw == 0 && dh == 0)
            return;

        var bounds = GetWorkingAreaDips();
        if (bounds is null) return;
        var screenCenter = new Point(
            bounds.Value.X + bounds.Value.Width / 2,
            bounds.Value.Y + bounds.Value.Height / 2);

        ResizeAnchorFlags flags;

        if (_previousResizeAnchor.HasValue)
        {
            flags = _previousResizeAnchor.Value;
            _previousResizeAnchor = null;
        }
        else
        {
            var (leftDip, topDip) = GetPositionDips();
            flags = ComputeResizeAnchorFlags(leftDip, topDip, width, height, screenCenter);
            _previousResizeAnchor = flags;
        }

        var (left, top) = GetPositionDips();

        if (flags.HasFlag(ResizeAnchorFlags.SpansX))
            left -= dw / 2;
        else if (flags.HasFlag(ResizeAnchorFlags.TopRightQuadrant) ||
                 flags.HasFlag(ResizeAnchorFlags.BottomRightQuadrant))
            left -= dw;

        if (flags.HasFlag(ResizeAnchorFlags.SpansY))
            top -= dh / 2;
        else if (flags.HasFlag(ResizeAnchorFlags.BottomLeftQuadrant) ||
                 flags.HasFlag(ResizeAnchorFlags.BottomRightQuadrant))
            top -= dh;

        SetPositionDips(left, top);
    }

    private static ResizeAnchorFlags ComputeResizeAnchorFlags(
        double left, double top, double width, double height, Point screenCenter)
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

    // ------------------------------------------------------------------
    // Hotkeys.

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.H)
        {
            _previousResizeAnchor = null;
            CenterHorizontally();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            _previousResizeAnchor = null;
            CenterVertically();
            e.Handled = true;
        }
        else if (e.Key == Key.A)
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            Topmost = _settings.AlwaysOnTop;
            OnSettingsChanged();
            e.Handled = true;
        }
        else if (e.Key == Key.T)
        {
            ViewModel.ToggleExpandCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------
    // Settings flyout (right-click). A Flyout with plain controls is used
    // instead of a ContextMenu so the sliders remain fully interactive.

    private Flyout BuildFlyout()
    {
        var flyout = new Flyout();
        var panel = new StackPanel();

        // Always on top
        var onTop = new CheckBox
        {
            Content = MakeMenuRow("Always on top", "A"),
            IsChecked = _settings.AlwaysOnTop
        };
        onTop.Classes.Add("menu");
        onTop.IsCheckedChanged += (_, _) =>
        {
            if (_syncingFlyout) return;
            _settings.AlwaysOnTop = onTop.IsChecked == true;
            Topmost = _settings.AlwaysOnTop;
            OnSettingsChanged();
        };
        panel.Children.Add(onTop);

        // Poll interval
        panel.Children.Add(MakeHeader("Poll interval"));
        var pollPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        var pollSlider = new Slider
        {
            Minimum = 5,
            Maximum = 60,
            Value = _settings.PollIntervalSeconds,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 120
        };
        pollSlider.Classes.Add("menu");
        var pollLabel = MakeValueLabel($"{(int)_settings.PollIntervalSeconds}s");
        pollSlider.ValueChanged += (_, e) =>
        {
            pollLabel.Text = $"{(int)e.NewValue}s";
            if (_syncingFlyout) return;
            _settings.PollIntervalSeconds = e.NewValue;
            ViewModel.SetInterval(e.NewValue);
            OnSettingsChanged();
        };
        pollPanel.Children.Add(pollSlider);
        pollPanel.Children.Add(pollLabel);
        panel.Children.Add(pollPanel);

        // Opacity
        panel.Children.Add(MakeHeader("Opacity"));
        var opacityPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        var opacitySlider = new Slider
        {
            Minimum = 5,
            Maximum = 100,
            Value = _settings.Opacity * 100,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 120
        };
        opacitySlider.Classes.Add("menu");
        var opacityLabel = MakeValueLabel($"{(int)(_settings.Opacity * 100)}%");
        opacitySlider.ValueChanged += (_, e) =>
        {
            opacityLabel.Text = $"{(int)e.NewValue}%";
            if (_syncingFlyout) return;
            var val = e.NewValue / 100.0;
            _settings.Opacity = val;
            Opacity = val;
            OnSettingsChanged();
        };
        opacityPanel.Children.Add(opacitySlider);
        opacityPanel.Children.Add(opacityLabel);
        panel.Children.Add(opacityPanel);

        // Actions
        var centerHorizItem = MakeMenuButton("Center horizontally", "H");
        centerHorizItem.Click += (_, _) =>
        {
            flyout.Hide();
            _previousResizeAnchor = null;
            CenterHorizontally();
        };
        panel.Children.Add(centerHorizItem);

        var centerVertItem = MakeMenuButton("Center vertically", "V");
        centerVertItem.Click += (_, _) =>
        {
            flyout.Hide();
            _previousResizeAnchor = null;
            CenterVertically();
        };
        panel.Children.Add(centerVertItem);

        var hideItem = MakeMenuButton("Hide", null);
        hideItem.Click += (_, _) =>
        {
            flyout.Hide();
            Hide();
        };
        panel.Children.Add(hideItem);

        var exitItem = MakeMenuButton("Exit", null);
        exitItem.Click += (_, _) =>
        {
            IsExitRequested = true;
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
        panel.Children.Add(exitItem);

        // Re-sync control state every time the flyout opens (values may have
        // changed via hotkeys while it was closed).
        flyout.Opening += (_, _) =>
        {
            _syncingFlyout = true;
            try
            {
                onTop.IsChecked = _settings.AlwaysOnTop;
                pollSlider.Value = _settings.PollIntervalSeconds;
                opacitySlider.Value = _settings.Opacity * 100;
            }
            finally
            {
                _syncingFlyout = false;
            }
        };

        flyout.Content = panel;
        return flyout;
    }

    private Control MakeMenuRow(string text, string? hint)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        if (!string.IsNullOrEmpty(hint))
        {
            var hintBlock = new TextBlock
            {
                Text = hint,
                Foreground = _onSurfaceVariant,
                Margin = new Thickness(24, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hintBlock, 1);
            grid.Children.Add(hintBlock);
        }

        return grid;
    }

    private Button MakeMenuButton(string text, string? hint)
    {
        var button = new Button { Content = MakeMenuRow(text, hint) };
        button.Classes.Add("menu");
        return button;
    }

    private Control MakeHeader(string text)
    {
        var header = new TextBlock { Text = text, FontFamily = _uiFont };
        header.Classes.Add("menuheader");
        return header;
    }

    private TextBlock MakeValueLabel(string text) => new()
    {
        Text = text,
        Width = 32,
        Margin = new Thickness(10, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = _onSurface,
        FontFamily = _uiFont,
        FontSize = 12
    };
}
