using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenCodeCostMeter;

/// <summary>
/// Minimal cross-platform replacement for the WPF MessageBox shown when the
/// opencode database cannot be found.
/// </summary>
public sealed class DatabaseNotFoundWindow : Window
{
    public DatabaseNotFoundWindow(string message)
    {
        Title = "OpenCode Cost Meter";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#20201F"));

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#E5E2E1")),
            Margin = new Avalonia.Thickness(0, 0, 0, 16)
        };

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 80
        };
        okButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children = { text, okButton }
        };
    }
}
