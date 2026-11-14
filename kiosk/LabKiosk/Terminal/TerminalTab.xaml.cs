using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using LabKiosk.Services;
using Microsoft.Web.WebView2.Core;

namespace LabKiosk.Terminal;

/// <summary>What a tab runs. Fixed by settings; Prepare runs before each start (e.g. wait for opencode serve).</summary>
public sealed record LaunchSpec(string Exe, string Args, string Cwd, IReadOnlyDictionary<string, string?> Env, Func<Task>? Prepare = null);

/// <summary>A real terminal: xterm.js in WebView2, bridged to a ConPTY session.</summary>
public partial class TerminalTab : UserControl, IDisposable
{
    private ConPtySession? _session;
    private LaunchSpec? _spec;
    private bool _pageReady, _locked, _starting, _isFocusing;
    private short _cols = 100, _rows = 30;
    private readonly StringBuilder _pending = new();
    private readonly object _gate = new();
    private readonly DispatcherTimer _flush;

    /// <summary>Raised on a background thread with every chunk the program prints (used for quota detection).</summary>
    public event Action<string>? OutputText;

    public TerminalTab()
    {
        InitializeComponent();
        _flush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _flush.Tick += (_, _) => FlushOutput();
        _flush.Start();
        PreviewMouseDown += (_, _) => FocusTerminal();

        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 50 && ActualHeight > 50 && Web.CoreWebView2 != null)
            {
                _ = Web.CoreWebView2.ExecuteScriptAsync("if (window.ensureFitted) window.ensureFitted();");
            }
        };

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue && Web.CoreWebView2 != null)
            {
                _ = Web.CoreWebView2.ExecuteScriptAsync("if (window.ensureFitted) window.ensureFitted(); if (window.term) { window.term.focus(); if (window.term.textarea) window.term.textarea.focus(); }");
            }
        };
    }

    public async Task InitializeAsync(string assetsDir)
    {
        await Web.EnsureCoreWebView2Async(await WebViewEnv.GetAsync());
        var core = Web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping("terminal.local", assetsDir, CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += OnWebMessage;
        Web.NavigationCompleted += (_, _) => FocusTerminal();
        core.Navigate("https://terminal.local/terminal.html");
    }

    public void FocusTerminal()
    {
        if (_isFocusing) return;
        _isFocusing = true;
        try
        {
            if (!Web.IsKeyboardFocusWithin)
            {
                Web.Focus();
                System.Windows.Input.Keyboard.Focus(Web);
            }
            Post(new { type = "focus" });
            if (Web.CoreWebView2 is not null)
            {
                _ = Web.CoreWebView2.ExecuteScriptAsync("if (window.term) { window.term.focus(); if (window.term.textarea) window.term.textarea.focus(); }");
            }
        }
        catch { }
        finally { _isFocusing = false; }
    }

    public void ForceFitAndRedraw()
    {
        if (Web.CoreWebView2 is null) return;
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            "if (window.ensureFitted) window.ensureFitted(); " +
            "if (window.fit) try { window.fit.fit(); } catch {} " +
            "if (window.term) { window.term.focus(); if (window.term.textarea) window.term.textarea.focus(); }"
        );
        if (_session is not null)
        {
            try
            {
                _session.Resize((short)(_cols + 1), _rows);
                _session.Resize(_cols, _rows);
            }
            catch { }
        }
    }

    public void Launch(LaunchSpec spec)
    {
        _spec = spec;
        if (_pageReady) _ = StartSessionAsync();
    }

    public void Lock(string text, bool disposeSession = false)
    {
        _locked = true;
        if (disposeSession)
        {
            _session?.Dispose();
            _session = null;
        }
        Post(new { type = "locked", text });
    }

    public void Unlock()
    {
        _locked = false;
        Post(new { type = "unlocked" });
    }

    public void UnlockAndRestart()
    {
        _locked = false;
        try { _session?.Dispose(); } catch { }
        _session = null;
        Post(new { type = "unlocked" });
        if (_pageReady && _spec is not null)
        {
            _ = StartSessionAsync();
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                var reportedCols = (short)root.GetProperty("cols").GetInt32();
                var reportedRows = (short)root.GetProperty("rows").GetInt32();
                if (reportedCols > 0) _cols = reportedCols;
                if (reportedRows > 0) _rows = reportedRows;
                _pageReady = true;
                _ = StartSessionAsync();
                break;
            case "focus_request":
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isFocusing) return;
                    _isFocusing = true;
                    try
                    {
                        if (!Web.IsKeyboardFocusWithin)
                        {
                            Web.Focus();
                            System.Windows.Input.Keyboard.Focus(Web);
                        }
                    }
                    catch { }
                    finally { _isFocusing = false; }
                });
                break;
            case "input":
                if (!_locked) _session?.Write(root.GetProperty("data").GetString() ?? "");
                break;
            case "resize":
                var newCols = (short)root.GetProperty("cols").GetInt32();
                var newRows = (short)root.GetProperty("rows").GetInt32();
                if (newCols > 0) _cols = newCols;
                if (newRows > 0) _rows = newRows;
                _session?.Resize(_cols, _rows);
                break;
            case "restart":
                if (!_locked) _ = StartSessionAsync();
                break;
        }
    }

    private async Task StartSessionAsync()
    {
        if (_locked || _spec is null || !_pageReady || _starting) return;
        _starting = true;
        try
        {
            _session?.Dispose();
            Post(new { type = "clear" });
            Post(new { type = "status", text = "Starting..." });
            if (_spec.Prepare is not null) await _spec.Prepare();
            if (_locked) return;
            var session = ConPtySession.Start(_spec.Exe, _spec.Args, _spec.Cwd, _spec.Env, _cols, _rows);
            var mySession = session;
            session.Output += text =>
            {
                OutputText?.Invoke(text);
                lock (_gate) _pending.Append(text);
            };
            session.Exited += code => Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_session, mySession) && !_locked)
                {
                    Post(new { type = "ended", code });
                }
            });
            _session = session;
            Post(new { type = "started" });

            // Kick pseudo console dimensions to ensure immediate initial TUI screen render
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                session.Resize(_cols, _rows);
                await Task.Delay(250);
                session.Resize(_cols, _rows);
            });
        }
        catch (Exception ex)
        {
            Post(new { type = "status", text = $"Could not start: {ex.Message}" });
        }
        finally { _starting = false; }
    }

    private void FlushOutput()
    {
        string chunk;
        lock (_gate)
        {
            if (_pending.Length == 0) return;
            chunk = _pending.ToString();
            _pending.Clear();
        }
        Post(new { type = "output", data = chunk });
    }

    private void Post(object message)
    {
        if (Web.CoreWebView2 is null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    public void Dispose()
    {
        _flush.Stop();
        _session?.Dispose();
    }
}
