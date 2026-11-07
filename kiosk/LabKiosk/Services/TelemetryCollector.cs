using System.IO;
using System.Text.RegularExpressions;

namespace LabKiosk.Services;

/// <summary>
/// Periodically scans local AI CLI history (.gemini, .opencode) and submits aggregate usage metrics.
/// STRICT PRIVACY BOUNDARY: Never transmits prompt text, completion text, or file paths. Only aggregate counts and estimated tokens are sent.
/// </summary>
public sealed class TelemetryCollector : IDisposable
{
    private readonly ApiClient _api;
    private readonly string _participantId;
    private readonly string _deskId;
    private Timer? _timer;
    private int _lastPromptCount = -1;
    private int _lastTokenEstimate = -1;
    private DateTime _lastPromptTime = DateTime.UtcNow;

    public TelemetryCollector(ApiClient api, string participantId, string deskId)
    {
        _api = api;
        _participantId = participantId;
        _deskId = deskId;
    }

    public void Start(TimeSpan interval)
    {
        _timer = new Timer(async _ => await TryCollectAndSyncAsync(), null, TimeSpan.Zero, interval);
    }

    private async Task TryCollectAndSyncAsync()
    {
        try
        {
            var (promptCount, tokenEstimate) = CollectMetrics();
            if (promptCount != _lastPromptCount || tokenEstimate != _lastTokenEstimate)
            {
                var now = DateTime.UtcNow;
                var cadenceSeconds = (now - _lastPromptTime).TotalSeconds;
                _lastPromptCount = promptCount;
                _lastTokenEstimate = tokenEstimate;
                _lastPromptTime = now;

                var payload = new
                {
                    desk_id = _deskId,
                    captured_at = now.ToString("O"),
                    prompt_count = promptCount,
                    token_usage = tokenEstimate,
                    cadence = Math.Round(cadenceSeconds, 1)
                };

                await _api.SendTelemetryAsync(_participantId, "CLI_METRICS", payload);
            }
        }
        catch
        {
            // Transient disk read or network error; retried on next tick
        }
    }

    private (int PromptCount, int TokenEstimate) CollectMetrics()
    {
        int promptCount = 0;
        int tokenEstimate = 0;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. .gemini history or transcripts
        var geminiDir = Path.Combine(userProfile, ".gemini");
        if (Directory.Exists(geminiDir))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(geminiDir, "*.json*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    if (info.Length > 0 && info.Length < 10 * 1024 * 1024)
                    {
                        var text = File.ReadAllText(file);
                        var count = Regex.Matches(text, "\"prompt\"|\"user_input\"|\"role\":\\s*\"user\"").Count;
                        if (count > 0)
                        {
                            promptCount += count;
                            tokenEstimate += text.Length / 4;
                        }
                    }
                }
            }
            catch { }
        }

        // 2. .opencode sessions
        var opencodeDir = Path.Combine(userProfile, ".opencode");
        if (Directory.Exists(opencodeDir))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(opencodeDir, "*.*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    if (info.Length > 0 && info.Length < 10 * 1024 * 1024)
                    {
                        var text = File.ReadAllText(file);
                        var count = Regex.Matches(text, "\"message\"|\"user\"|\"prompt\"").Count;
                        if (count > 0)
                        {
                            promptCount += count;
                            tokenEstimate += text.Length / 4;
                        }
                    }
                }
            }
            catch { }
        }

        return (promptCount, tokenEstimate);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

