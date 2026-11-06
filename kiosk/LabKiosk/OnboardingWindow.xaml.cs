using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LabKiosk.Services;

namespace LabKiosk;

public partial class OnboardingWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly KioskSettings _settings;
    private readonly DispatcherTimer _pollTimer;

    public OnboardingWindow(KioskSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        TxtDisplayName.Text = string.IsNullOrWhiteSpace(_settings.DisplayName) || _settings.DisplayName == Environment.MachineName
            ? ""
            : _settings.DisplayName;

        TxtServerUrl.Text = string.IsNullOrWhiteSpace(_settings.ServerUrl)
            ? "http://127.0.0.1:5080"
            : _settings.ServerUrl;

        TxtGithubHandle.Text = _settings.GitHubHandle;
        TxtGoogleEmail.Text = _settings.GoogleAccount;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => CheckGoogleAuth();
        _pollTimer.Start();

        Loaded += (_, _) =>
        {
            CheckGoogleAuth();
            if (!string.IsNullOrWhiteSpace(_settings.GitHubHandle))
                _ = VerifyGithubHandleAsync(_settings.GitHubHandle);
        };

        Closed += (_, _) => _pollTimer.Stop();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CheckGoogleAuth()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var geminiDir = Path.Combine(userProfile, ".gemini");
            var accountsFile = Path.Combine(geminiDir, "google_accounts.json");
            var cliSettings = Path.Combine(geminiDir, "antigravity-cli", "settings.json");

            string? detectedEmail = null;

            if (File.Exists(accountsFile))
            {
                var content = File.ReadAllText(accountsFile);
                if (content.Contains('@'))
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("active_account", out var emailProp))
                        detectedEmail = emailProp.GetString();
                    else if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        detectedEmail = doc.RootElement[0].GetProperty("email").GetString();
                }
            }

            if (detectedEmail is null && !string.IsNullOrWhiteSpace(_settings.GoogleAccount))
                detectedEmail = _settings.GoogleAccount;

            if (!string.IsNullOrWhiteSpace(detectedEmail))
            {
                TxtGoogleEmail.Text = detectedEmail;
                TxtGoogleStatus.Text = "✔ Connected: " + detectedEmail;
                TxtGoogleStatus.Foreground = (Brush)FindResource("Ok");
                BadgeGoogle.BorderBrush = (Brush)FindResource("Ok");
                BadgeGoogle.Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139));
            }
        }
        catch { /* best effort */ }
    }

    private void OnGoogleLoginClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var agy = ExeResolver.Resolve(_settings.AgyPath);
            if (agy is not null)
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{agy}\" auth login\"")
                {
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://accounts.google.com",
                    UseShellExecute = true
                });
            }
            TxtGoogleStatus.Text = "⏳ Awaiting browser sign-in...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Google sign in: {ex.Message}", "Sign In", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnHandleChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDisplayName.Text) || TxtDisplayName.Text.StartsWith('@'))
        {
            var h = TxtGithubHandle.Text.Trim().TrimStart('@');
            if (!string.IsNullOrWhiteSpace(h))
                TxtDisplayName.Text = "@" + h;
        }
    }

    private void OnGithubDeviceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/login",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void OnVerifyGithubClick(object sender, RoutedEventArgs e)
    {
        var handle = TxtGithubHandle.Text.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(handle))
        {
            MessageBox.Show("Please enter your GitHub handle first.", "GitHub Handle Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await VerifyGithubHandleAsync(handle);
    }

    private async Task VerifyGithubHandleAsync(string handle)
    {
        try
        {
            TxtGithubStatus.Text = "⏳ Verifying...";
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/users/{handle}");
            req.Headers.Add("User-Agent", "LabKiosk-BreachProtocol/1.0");
            using var res = await Http.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var login = doc.RootElement.GetProperty("login").GetString() ?? handle;

                TxtGithubHandle.Text = "@" + login;
                TxtGithubStatus.Text = "✔ Verified: @" + login;
                TxtGithubStatus.Foreground = (Brush)FindResource("Ok");
                BadgeGithub.BorderBrush = (Brush)FindResource("Ok");
                BadgeGithub.Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139));

                if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
                    TxtDisplayName.Text = "@" + login;
            }
            else
            {
                TxtGithubStatus.Text = "⚠ Handle not found on GitHub";
                TxtGithubStatus.Foreground = (Brush)FindResource("Alert");
                BadgeGithub.BorderBrush = (Brush)FindResource("Alert");
            }
        }
        catch
        {
            // Offline fallback: accept handle without blocking
            TxtGithubStatus.Text = "✔ Registered: @" + handle;
            TxtGithubStatus.Foreground = (Brush)FindResource("Ok");
        }
    }

    private async void OnTestServerClick(object sender, RoutedEventArgs e)
    {
        var raw = TxtServerUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Contains(':') ? "http://" + raw : "http://" + raw + ":5080";
            TxtServerUrl.Text = raw;
        }

        try
        {
            TxtServerStatus.Text = "Testing...";
            TxtServerStatus.Foreground = (Brush)FindResource("Mute");

            using var res = await Http.GetAsync(raw.TrimEnd('/') + "/api/state");
            if (res.IsSuccessStatusCode)
            {
                TxtServerStatus.Text = "✔ Connected to Organizer";
                TxtServerStatus.Foreground = (Brush)FindResource("Ok");
            }
            else
            {
                TxtServerStatus.Text = "⚠ Server returned " + (int)res.StatusCode;
                TxtServerStatus.Foreground = (Brush)FindResource("Alert");
            }
        }
        catch (Exception ex)
        {
            TxtServerStatus.Text = "❌ Unreachable: " + ex.Message;
            TxtServerStatus.Foreground = (Brush)FindResource("Alert");
        }
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        var name = TxtDisplayName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TxtWarning.Text = "Please enter a Leaderboard Display Name.";
            return;
        }

        var url = TxtServerUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            TxtWarning.Text = "Please enter the Organizer Server URL.";
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Contains(':') ? "http://" + url : "http://" + url + ":5080";
        }

        _settings.DisplayName = name;
        _settings.ServerUrl = url;
        _settings.GitHubHandle = TxtGithubHandle.Text.Trim();
        _settings.GoogleAccount = TxtGoogleEmail.Text.Trim();
        _settings.Onboarded = true;

        SaveSettings();

        DialogResult = true;
        Close();
    }

    private void SaveSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
            var doc = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path);
                    doc = JsonSerializer.Deserialize<Dictionary<string, object>>(existing) ?? new();
                }
                catch { }
            }

            doc["DisplayName"] = _settings.DisplayName;
            doc["ServerUrl"] = _settings.ServerUrl;
            doc["GitHubHandle"] = _settings.GitHubHandle;
            doc["GoogleAccount"] = _settings.GoogleAccount;
            doc["Onboarded"] = true;
            doc["OpenCodeCap"] = _settings.OpenCodeCap;

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* best effort */ }
    }
}
