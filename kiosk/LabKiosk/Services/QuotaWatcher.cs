using System.Text.RegularExpressions;

namespace LabKiosk.Services;

/// <summary>Reads agy's terminal output for the quota-exhausted message (plan section 7, signal pending spike S3).</summary>
public sealed partial class QuotaWatcher
{
    private static readonly string[] ExhaustedMarkers = { "individual quota reached", "resource_exhausted", "quota exceeded" };
    private string _tail = "";
    private bool _fired;

    public event Action? Exhausted;
    public event Action<string>? OtherSignal;

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)")]
    private static partial Regex Ansi();

    /// <summary>Safe to call from the reader thread.</summary>
    public void Feed(string chunk)
    {
        lock (this)
        {
            _tail = (_tail + Ansi().Replace(chunk, "")).ToLowerInvariant();
            if (_tail.Length > 4096) _tail = _tail[^4096..];
            if (!_fired && ExhaustedMarkers.Any(_tail.Contains))
            {
                _fired = true;
                Exhausted?.Invoke();
            }
            else if (_tail.Contains("gemini api key") && !_fired) OtherSignal?.Invoke("api_key_mode");
        }
    }
}
