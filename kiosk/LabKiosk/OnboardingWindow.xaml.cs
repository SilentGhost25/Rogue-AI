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
    private bool _googleConnected;
    private bool _githubConnected;

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

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _pollTimer.Tick += (_, _) => PollCredentials();
        _pollTimer.Start();

        Loaded += (_, _) =>
        {
            CheckGoogleAuth();
            CheckGithubAuth(force: false);
        };

        Closed += (_, _) => _pollTimer.Stop();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PollCredentials()
    {
        if (!_googleConnected)
            CheckGoogleAuth();

        if (!_githubConnected)
            CheckGithubAuth(force: false);
    }

    // ---- Google / Antigravity Authentication --------------------------------------------------------
    private void CheckGoogleAuth()
    {
        try
        {
            var detectedEmail = CredentialManager.GetAntigravityGoogleEmail();
            if (string.IsNullOrWhiteSpace(detectedEmail) && !string.IsNullOrWhiteSpace(_settings.GoogleAccount))
                detectedEmail = _settings.GoogleAccount;

            if (!string.IsNullOrWhiteSpace(detectedEmail))
            {
                _googleConnected = true;
                _settings.GoogleAccount = detectedEmail;
                TxtGoogleEmail.Text = detectedEmail;
                TxtGoogleStatus.Text = "✔ Connected: " + detectedEmail;
                TxtGoogleStatus.Foreground = (Brush)FindResource("Ok");
                BadgeGoogle.BorderBrush = (Brush)FindResource("Ok");
                BadgeGoogle.Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139));
                BtnGoogleLogin.Content = "🔄 Switch Google Account";
            }
        }
        catch { /* best effort */ }
    }

    private void OnGoogleLoginClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_googleConnected)
            {
                var res = MessageBox.Show(
                    "Antigravity is currently authenticated with Google:\n" + TxtGoogleEmail.Text +
                    "\n\nDo you want to switch accounts? This will clear the current session so you can log in with a different Google account.",
                    "Switch Google Account",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                CredentialManager.ClearAntigravityGoogleCredential();
                _googleConnected = false;
                TxtGoogleEmail.Text = "";
                TxtGoogleStatus.Text = "⏳ Awaiting sign-in...";
                TxtGoogleStatus.Foreground = (Brush)FindResource("Mute");
                BadgeGoogle.BorderBrush = (Brush)FindResource("Mute");
                BadgeGoogle.Background = new SolidColorBrush(Color.FromArgb(20, 27, 42, 31));
                BtnGoogleLogin.Content = "🌐 Sign In with Google";
            }

            var agy = ExeResolver.Resolve(_settings.AgyPath) ?? ExeResolver.Resolve("agy");
            if (agy is not null)
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var banner = "title ROGUE AI - Antigravity Sign In && " +
                             "echo ======================================================== && " +
                             "echo ROGUE AI: Antigravity AI Google Authentication && " +
                             "echo Sign in to your Google Account in the browser when prompted. && " +
                             "echo Once sign-in completes, this window can be closed. && " +
                             "echo ======================================================== && ";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k {banner}\"{agy}\" --log-file \"%TEMP%\\agy_session.log\"",
                    WorkingDirectory = userProfile,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                TxtGoogleStatus.Text = "⏳ Complete Google sign-in in browser...";
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://accounts.google.com",
                    UseShellExecute = true
                });
                TxtGoogleStatus.Text = "⏳ Browser opened. Sign in to Google...";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start Google sign in: {ex.Message}", "Sign In Problem", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- GitHub Authentication & Verification -------------------------------------------------------
    private void CheckGithubAuth(bool force)
    {
        try
        {
            var detectedUser = CredentialManager.GetGitHubUsername();
            if (!string.IsNullOrWhiteSpace(detectedUser))
            {
                if (force || string.IsNullOrWhiteSpace(TxtGithubHandle.Text) || !_githubConnected)
                {
                    TxtGithubHandle.Text = "@" + detectedUser;
                    _ = VerifyGithubHandleAsync(detectedUser);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_settings.GitHubHandle) && !_githubConnected)
            {
                _ = VerifyGithubHandleAsync(_settings.GitHubHandle);
            }
        }
        catch { /* best effort */ }
    }

    private void OnHandleChanged(object sender, TextChangedEventArgs e)
    {
        var h = TxtGithubHandle.Text.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(TxtDisplayName.Text) || TxtDisplayName.Text.StartsWith('@'))
        {
            if (!string.IsNullOrWhiteSpace(h))
                TxtDisplayName.Text = "@" + h;
        }
    }

    private void OnGithubDeviceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var auth = new GitHubAuthWindow { Owner = this };
            if (auth.ShowDialog() == true && !string.IsNullOrWhiteSpace(auth.AuthenticatedUsername))
            {
                TxtGithubHandle.Text = "@" + auth.AuthenticatedUsername;
                _ = VerifyGithubHandleAsync(auth.AuthenticatedUsername);
                return;
            }
        }
        catch
        {
            // Fallback to opening system browser if WebView2 modal encounters an issue
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/login",
                    UseShellExecute = true
                });
            }
            catch { }
            TxtGithubStatus.Text = "⏳ Browser opened. Enter your handle and click Verify.";
        }
    }

    private void OnAutoDetectGithubClick(object sender, RoutedEventArgs e)
    {
        var detected = CredentialManager.GetGitHubUsername();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            TxtGithubHandle.Text = "@" + detected;
            _ = VerifyGithubHandleAsync(detected);
        }
        else
        {
            MessageBox.Show(
                "No saved GitHub credential found in Windows.\n\n" +
                "If you just signed into GitHub in your browser, please enter your username (e.g. Prince649294u83) in the text box and click 'Verify Handle'.",
                "GitHub Auto-Detect",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
                string? realName = null;
                if (doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    realName = nameProp.GetString();

                _githubConnected = true;
                _settings.GitHubHandle = login;
                TxtGithubHandle.Text = "@" + login;
                TxtGithubStatus.Text = string.IsNullOrWhiteSpace(realName)
                    ? "✔ Verified: @" + login
                    : $"✔ Verified: @{login} ({realName})";
                TxtGithubStatus.Foreground = (Brush)FindResource("Ok");
                BadgeGithub.BorderBrush = (Brush)FindResource("Ok");
                BadgeGithub.Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139));

                if (string.IsNullOrWhiteSpace(TxtDisplayName.Text) || TxtDisplayName.Text.StartsWith('@'))
                    TxtDisplayName.Text = login;
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
            // Offline fallback: accept handle without blocking campus event
            _githubConnected = true;
            _settings.GitHubHandle = handle;
            TxtGithubStatus.Text = "✔ Registered: @" + handle;
            TxtGithubStatus.Foreground = (Brush)FindResource("Ok");
            BadgeGithub.BorderBrush = (Brush)FindResource("Ok");
            BadgeGithub.Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139));

            if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
                TxtDisplayName.Text = handle;
        }
    }

    // ---- Server Connection & Launch -----------------------------------------------------------------
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
        _settings.GitHubHandle = TxtGithubHandle.Text.Trim().TrimStart('@');
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
