using System.Text.RegularExpressions;

namespace LabKiosk.Services;

/// <summary>Reads agy's terminal output for the quota-exhausted message (plan section 7, signal pending spike S3).</summary>
public sealed partial class QuotaWatcher
{
    private static readonly string[] ExhaustedMarkers = { "individual quota reached", "resource_exhausted", "quota exceeded" };
    private static readonly string[] ProOrUnapprovedModelMarkers =
    {
        "gemini api key",
        "claude-opus",
        "claude-sonnet",
        "gemini-1.5-pro",
        "gemini 1.5 pro",
        "gemini-pro",
        "gpt-4",
        "gpt-4o",
        "deepseek-reasoner"
    };
    private readonly HashSet<string> _reportedSignals = new(StringComparer.OrdinalIgnoreCase);
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

            foreach (var marker in ProOrUnapprovedModelMarkers)
            {
                if (_tail.Contains(marker) && _reportedSignals.Add(marker))
                {
                    var signal = marker == "gemini api key" ? "api_key_mode" : $"unapproved_model:{marker}";
                    OtherSignal?.Invoke(signal);
                }
            }
        }
    }
}
