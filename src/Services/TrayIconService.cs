using System.Windows;
using Forms = System.Windows.Forms;

namespace OpenCodeCostMeter.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Window _mainWindow;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(Window mainWindow, string tooltip, Action exitApplication)
    {
        _mainWindow = mainWindow;

        var showItem = new Forms.ToolStripMenuItem("Show");
        showItem.Click += (_, _) => ShowWindow();

        var hideItem = new Forms.ToolStripMenuItem("Hide");
        hideItem.Click += (_, _) => _mainWindow.Hide();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => exitApplication()));

        var icon = LoadIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = tooltip,
            Icon = icon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += OnIconDoubleClick;
    }

    private static Icon LoadIcon()
    {
        const string iconUri = "pack://application:,,,/Assets/icon.ico";
        var streamInfo = System.Windows.Application.GetResourceStream(new Uri(iconUri));
        if (streamInfo != null)
        {
            return new Icon(streamInfo.Stream);
        }

        // Fallback if the embedded icon is missing.
        return new Icon(SystemIcons.Application, 16, 16);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void OnIconDoubleClick(object? sender, EventArgs e)
    {
        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            ShowWindow();
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
}
