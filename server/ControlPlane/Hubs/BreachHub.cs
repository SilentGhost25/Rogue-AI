using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Hubs;

/// <summary>Kiosks join their own group; admin pages join "admins".</summary>
public sealed class BreachHub(IConfiguration config) : Hub
{
    public Task RegisterKiosk(string participantId) => Groups.AddToGroupAsync(Context.ConnectionId, $"p:{participantId}");

    public Task JoinAdmin(string adminKey)
        => adminKey == config["Breach:AdminKey"] ? Groups.AddToGroupAsync(Context.ConnectionId, "admins") : Task.CompletedTask;

    /// <summary>Kiosk confirms it flushed a final snapshot after BREACH_IMMINENT.</summary>
    public Task AckImminent(int attackId, string participantId)
        => Clients.Group("admins").SendAsync("ack", attackId, participantId);
}

public static class HubEvents
{
    public const string BreachImminent = "BREACH_IMMINENT";
    public const string AttackLanded = "ATTACK_LANDED";
    public const string AttackReset = "ATTACK_RESET";
    public const string Broadcast = "BROADCAST";
    public const string Paused = "PAUSED";
    public const string Resumed = "RESUMED";
    public const string Verdict = "VERDICT";
    public const string Eliminated = "ELIMINATED";
}
