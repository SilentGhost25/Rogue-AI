using System.IO;
using System.Text.Json;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class ServerConfigDialog : Window
{
    public string ServerUrl { get; private set; }
    public string DeskId { get; private set; }

    public ServerConfigDialog(string currentServerUrl, string currentDeskId)
    {
        InitializeComponent();
        ServerUrl = currentServerUrl;
        DeskId = currentDeskId;

        ServerUrlBox.Text = currentServerUrl;
        DeskIdBox.Text = currentDeskId;

        Loaded += (_, _) => ServerUrlBox.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var rawUrl = ServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            MessageBox.Show("Please enter a valid server URL or IP address.", "Invalid Server URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = "http://" + rawUrl;
        }

        if (!rawUrl.Contains(":", StringComparison.OrdinalIgnoreCase) || rawUrl.EndsWith("://"))
        {
            rawUrl = rawUrl.TrimEnd('/') + ":5080";
        }

        var desk = DeskIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(desk))
        {
            desk = "DESK-01";
        }

        ServerUrl = rawUrl;
        DeskId = desk;

        // Persist to kiosk.json
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
            var settings = KioskSettings.Load();
            settings.ServerUrl = ServerUrl;
            settings.DeskId = DeskId;

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
        catch { }

        DialogResult = true;
        Close();
    }
}

