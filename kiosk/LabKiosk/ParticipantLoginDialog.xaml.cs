using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class ParticipantLoginDialog : Window
{
    public string ParticipantName { get; private set; } = "";
    public string DeskId { get; private set; } = "DESK-01";
    public string Password { get; private set; } = "";
    public string ServerUrl { get; private set; } = "http://127.0.0.1:5080";

    public ParticipantLoginDialog(KioskSettings settings)
    {
        InitializeComponent();

        ParticipantName = !string.IsNullOrWhiteSpace(settings.DisplayName) && settings.DisplayName != Environment.MachineName
            ? settings.DisplayName
            : "";
        DeskId = !string.IsNullOrWhiteSpace(settings.DeskId) ? settings.DeskId : "DESK-01";
        ServerUrl = !string.IsNullOrWhiteSpace(settings.ServerUrl) ? settings.ServerUrl : "http://127.0.0.1:5080";

        ParticipantNameBox.Text = ParticipantName;
        DeskIdBox.Text = DeskId;
        ServerUrlBox.Text = ServerUrl;

        Loaded += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(ParticipantNameBox.Text))
            {
                ParticipantNameBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }

            // Quick background check or auto-discovery of host
            DiscoveryStatusText.Text = "Checking LAN...";
            var reachable = await NetworkDiscovery.ProbeHostAsync(ServerUrlBox.Text, 600);
            if (reachable)
            {
                DiscoveryStatusText.Text = "● Host Connected";
                DiscoveryStatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
            }
            else
            {
                DiscoveryStatusText.Text = "🔍 Scanning LAN...";
                var discovered = await NetworkDiscovery.AutoDiscoverHostAsync(ServerUrlBox.Text);
                if (!string.IsNullOrEmpty(discovered))
                {
                    ServerUrlBox.Text = discovered;
                    DiscoveryStatusText.Text = "● Host Found (" + discovered + ")";
                    DiscoveryStatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
                }
                else
                {
                    DiscoveryStatusText.Text = "⚠ Offline / Manual IP";
                    DiscoveryStatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                OnLogin(this, new RoutedEventArgs());
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                OnCancel(this, new RoutedEventArgs());
            }
        };
    }

    private async void OnScanLan(object sender, RoutedEventArgs e)
    {
        DiscoveryStatusText.Text = "🔍 Scanning...";
        var found = await NetworkDiscovery.AutoDiscoverHostAsync(null);
        if (!string.IsNullOrEmpty(found))
        {
            ServerUrlBox.Text = found;
            DiscoveryStatusText.Text = "● Discovered (" + found + ")";
            DiscoveryStatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
        }
        else
        {
            DiscoveryStatusText.Text = "⚠ Not found on LAN";
            DiscoveryStatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
            MessageBox.Show("Could not auto-detect Breach Command on local network.\nPlease enter the IP shown on the Organizer screen.", "Discovery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        var name = ParticipantNameBox.Text.Trim();
        var desk = DeskIdBox.Text.Trim();
        var rawUrl = ServerUrlBox.Text.Trim();
        var pwd = PasswordBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter your Participant or Team Name.", "Login Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            ParticipantNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(desk))
        {
            MessageBox.Show("Please enter your assigned Desk Identifier (e.g. DESK-01).", "Desk Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            DeskIdBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            rawUrl = "http://127.0.0.1:5080";
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

        ParticipantName = name;
        DeskId = desk;
        Password = pwd;
        ServerUrl = rawUrl;

        // Persist locally
        var settings = KioskSettings.Load();
        settings.DisplayName = ParticipantName;
        settings.DeskId = DeskId;
        settings.ServerUrl = ServerUrl;
        settings.Password = Password;
        settings.Save();

        DialogResult = true;
        Close();
    }
}

