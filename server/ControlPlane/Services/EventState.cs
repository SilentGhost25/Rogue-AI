namespace ControlPlane.Services;

/// <summary>In-memory event clock flags. Durable truth lives in the event log.</summary>
public sealed class EventState
{
    private readonly object _gate = new();
    public bool Paused { get; private set; }
    public DateTime? PausedAt { get; private set; }
    public bool Frozen { get; private set; }

    public void Pause() { lock (_gate) { Paused = true; PausedAt = DateTime.UtcNow; } }
    public void Resume() { lock (_gate) { Paused = false; PausedAt = null; } }
    public void ToggleFreeze() { lock (_gate) { Frozen = !Frozen; } }
    public void SetFreeze(bool freeze) { lock (_gate) { Frozen = freeze; } }
}
