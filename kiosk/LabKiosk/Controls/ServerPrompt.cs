using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LabKiosk.Controls;

/// <summary>
/// A dialog that allows changing the Organizer Server URL on the fly and saving it to kiosk.json.
/// </summary>
public static class ServerPrompt
{
    public static string? Ask(Window owner, string currentUrl)
    {
        var box = new TextBox
        {
            Text = currentUrl,
            Margin = new Thickness(0, 8, 0, 16),
            MinWidth = 320,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13
        };

        var hint = new TextBlock
        {
            Text = "Enter the Organizer Station's physical LAN IP (e.g. 192.168.1.50) or full URL.\nAvoid virtual Hyper-V/WSL addresses.",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };

        var ok = new Button
        {
            Content = "Connect",
            IsDefault = true,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure Organizer Server URL",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(hint);
        panel.Children.Add(box);
        panel.Children.Add(btnPanel);

        var win = new Window
        {
            Title = "Network Configuration",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        string? result = null;
        ok.Click += (_, _) =>
        {
            var raw = box.Text.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (raw.Contains(':'))
                        raw = "http://" + raw;
                    else
                        raw = "http://" + raw + ":5080";
                }
                result = raw;
            }
            win.Close();
        };

        win.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        win.ShowDialog();
        return result;
    }

    public static void SaveServerUrl(string newUrl)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            doc["ServerUrl"] = newUrl;
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}

