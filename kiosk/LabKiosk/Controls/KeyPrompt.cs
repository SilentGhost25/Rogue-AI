using System.Windows;
using System.Windows.Controls;

namespace LabKiosk.Controls;

/// <summary>A tiny modal that asks for the organizer key. Built in code so it needs no XAML file.</summary>
public static class KeyPrompt
{
    public static string? Ask(Window owner)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 12), MinWidth = 260 };
        var ok = new Button { Content = "Open", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Organizer key" });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var win = new Window
        {
            Title = "Admin Console", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Password; win.Close(); };
        win.Loaded += (_, _) => box.Focus();
        win.ShowDialog();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
