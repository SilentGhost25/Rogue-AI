namespace ControlPlane.Models;

public enum DispatchState
{
    Pending,
    CanaryFired,
    CanaryConfirmed,
    ArenaImminent,
    ArenaLanded,
    Failed
}

public class AttackDispatchStatus
{
    public int AttackId { get; set; }
    public string Name { get; set; } = "";
    public string Codename { get; set; } = "";
    public DispatchState State { get; set; }
    public string ScheduledTime { get; set; } = "";
    public string? UpdatedAt { get; set; }
    public string? DispatchedAt { get; set; }
}
