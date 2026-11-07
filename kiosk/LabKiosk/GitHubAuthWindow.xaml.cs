using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LabKiosk.Services;
using Microsoft.Web.WebView2.Core;

namespace LabKiosk;

public partial class GitHubAuthWindow : Window
{
    public string? AuthenticatedUsername { get; private set; }
    private DispatcherTimer? _pollTimer;

    public GitHubAuthWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitBrowserAsync();
        Closed += (_, _) => _pollTimer?.Stop();
    }

    private async Task InitBrowserAsync()
    {
        try
        {
            var env = await WebViewEnv.GetAsync();
            await AuthBrowser.EnsureCoreWebView2Async(env);

            LoadingOverlay.Visibility = Visibility.Collapsed;

            AuthBrowser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            AuthBrowser.Source = new Uri("https://github.com/login");

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _pollTimer.Tick += async (_, _) => await CheckForAuthenticatedUserAsync();
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            TxtLoading.Text = "Browser Error: " + ex.Message;
            TxtStatus.Text = "Could not initialize WebView2. You can enter your handle manually.";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        await CheckForAuthenticatedUserAsync();
    }

    private async Task CheckForAuthenticatedUserAsync()
    {
        try
        {
            if (AuthBrowser.CoreWebView2 == null) return;

            // 1. Inspect cookies for standard GitHub logged-in session marker (dotcom_user)
            var cookies = await AuthBrowser.CoreWebView2.CookieManager.GetCookiesAsync("https://github.com");
            var userCookie = cookies.FirstOrDefault(c => c.Name.Equals("dotcom_user", StringComparison.OrdinalIgnoreCase));
            if (userCookie != null && !string.IsNullOrWhiteSpace(userCookie.Value))
            {
                var candidate = userCookie.Value.Trim().TrimStart('@');
                if (IsValidHandle(candidate))
                {
                    CompleteAuthentication(candidate);
                    return;
                }
            }

            // 2. Inspect DOM for user meta tags and standard header controls
            const string script = @"(() => {
                const meta = document.querySelector('meta[name=""user-login""]');
                if (meta && meta.content) return meta.content.trim();
                const octo = document.querySelector('meta[name=""octolytics-actor-login""]');
                if (octo && octo.content) return octo.content.trim();
                const cookieMatch = document.cookie.match(/dotcom_user=([^; ]+)/);
                if (cookieMatch && cookieMatch[1]) return cookieMatch[1].trim();
                const span = document.querySelector('span.AppHeader-user');
                if (span && span.innerText) return span.innerText.trim();
                const img = document.querySelector('img.avatar-user');
                if (img && img.alt) return img.alt.replace('@', '').trim();
                return '';
            })()";

            var raw = await AuthBrowser.ExecuteScriptAsync(script);
            if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "\"\"")
            {
                var username = JsonSerializer.Deserialize<string>(raw)?.Trim().TrimStart('@');
                if (IsValidHandle(username))
                {
                    CompleteAuthentication(username!);
                    return;
                }
            }

            // 3. Fallback: check URL path on github.com
            var uri = AuthBrowser.Source;
            if (uri != null && uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath.Trim('/');
                if (!string.IsNullOrWhiteSpace(path) &&
                    !path.Contains('/') &&
                    !path.Equals("login", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("session", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("join", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("features", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("explore", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("settings", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("dashboard", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals("home", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsValidHandle(path))
                    {
                        CompleteAuthentication(path);
                    }
                }
            }
        }
        catch { /* best effort */ }
    }

    private static bool IsValidHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) return false;
        var h = handle.Trim().TrimStart('@');
        if (h.Length == 0 || h.Length > 39) return false;
        return !h.Equals("login", StringComparison.OrdinalIgnoreCase) &&
               !h.Equals("null", StringComparison.OrdinalIgnoreCase) &&
               !h.Equals("undefined", StringComparison.OrdinalIgnoreCase);
    }

    private void CompleteAuthentication(string username)
    {
        _pollTimer?.Stop();
        AuthenticatedUsername = username;
        TxtStatus.Text = $"✔ Authenticated as @{username}";
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _pollTimer?.Stop();
        DialogResult = false;
        Close();
    }
}
