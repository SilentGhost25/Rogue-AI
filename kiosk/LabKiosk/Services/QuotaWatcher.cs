using System.Text.RegularExpressions;

namespace LabKiosk.Services;

/// <summary>Reads agy's terminal output for the quota-exhausted message (plan section 7, signal pending spike S3).</summary>
public sealed partial class QuotaWatcher
{
    private static readonly string[] ExhaustedMarkers = {
        "resource_exhausted",
        "quota exceeded for quota metric",
        "quota exceeded",
        "exceeded your current quota",
        "rate limit exceeded",
        "error: [googlegenerativeai error]: 429",
        "terminal prompt error: quota",
        "individual quota reached",
        "out of credits",
        "ai: out of credits",
        "insufficient credits",
        "credit balance"
    };
    private string _tail = "";
    private bool _fired;
    private bool _proAccountFired;

    public bool InhibitExhaustion { get; set; }

    public event Action? Exhausted;
    public event Action<string>? OtherSignal;
    public event Action<string>? ProAccountDetected;

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"(?i)\(google\s+ai\s+pro\)|google\s+ai\s+pro|tier:\s*pro")]
    private static partial Regex ProAccountRegex();

    public void Reset()
    {
        lock (this)
        {
            _tail = "";
            _fired = false;
            _proAccountFired = false;
        }
    }

    /// <summary>Safe to call from the reader thread.</summary>
    public void Feed(string chunk)
    {
        lock (this)
        {
            var cleaned = Ansi().Replace(chunk, "");
            _tail = (_tail + cleaned).ToLowerInvariant();
            if (_tail.Length > 4096) _tail = _tail[^4096..];

            if (!_proAccountFired && ProAccountRegex().IsMatch(_tail))
            {
                _proAccountFired = true;
                ProAccountDetected?.Invoke("Google AI Pro Subscription Detected");
            }

            if (!InhibitExhaustion && !_fired && ExhaustedMarkers.Any(_tail.Contains))
            {
                _fired = true;
                Exhausted?.Invoke();
            }
            else if (_tail.Contains("gemini api key") && !_fired) OtherSignal?.Invoke("api_key_mode");
        }
    }
}
