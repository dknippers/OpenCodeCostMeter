using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenCodeCostMeter.Services;

/// <summary>
/// System tray icon backed by Avalonia's cross-platform <see cref="TrayIcon"/>.
/// Click toggles widget visibility (Win32/Linux; macOS does not raise the
/// click event — the menu is the interaction there). The menu can show or
/// hide the widget and exit the application.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Window _mainWindow;
    private readonly TrayIcon _trayIcon;

    public TrayIconService(Window mainWindow, string tooltip, Action exitApplication)
    {
        _mainWindow = mainWindow;

        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) => ShowWindow();

        var hideItem = new NativeMenuItem("Hide");
        hideItem.Click += (_, _) => _mainWindow.Hide();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => exitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = tooltip,
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += OnClicked;
    }

    private static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://OpenCodeCostMeter/Assets/icon.ico"));
        return new WindowIcon(stream);
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private void ShowWindow()
    {
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        _mainWindow.Activate();
    }

    public void Dispose()
    {
        _trayIcon.Clicked -= OnClicked;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
