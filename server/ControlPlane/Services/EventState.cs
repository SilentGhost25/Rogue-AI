namespace ControlPlane.Services;

/// <summary>Event clock flags. Global Pause freezes attack timers: paused time is subtracted from the schedule clock.</summary>
public sealed class EventState
{
    private readonly object _gate = new();
    private DateTime? _pausedAt;
    private TimeSpan _pausedTotal = TimeSpan.Zero;

    private volatile bool _frozen;
    /// <summary>Final-hour freeze: the projector masks the top five scores while ranks keep moving.</summary>
    public bool Frozen => _frozen;
    public void SetFrozen(bool value) => _frozen = value;

    public bool Paused { get { lock (_gate) return _pausedAt is not null; } }

    public TimeSpan PausedTotal
    {
        get { lock (_gate) return _pausedTotal + (_pausedAt is null ? TimeSpan.Zero : DateTime.UtcNow - _pausedAt.Value); }
    }

    /// <summary>Wall-clock time as the schedule sees it (local time minus all paused time).</summary>
    public DateTime ScheduleNow => DateTime.Now - PausedTotal;

    public void Pause() { lock (_gate) _pausedAt ??= DateTime.UtcNow; }

    public void Resume()
    {
        lock (_gate)
        {
            if (_pausedAt is null) return;
            _pausedTotal += DateTime.UtcNow - _pausedAt.Value;
            _pausedAt = null;
        }
    }
}
