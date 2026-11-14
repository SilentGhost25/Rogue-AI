using ControlPlane.Data;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Hubs;

/// <summary>Kiosks join their own group; admin pages join "admins"; projectors join "projectors".</summary>
public sealed class BreachHub(IConfiguration config, Database db) : Hub
{
    public async Task RegisterKiosk(string participantId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, participantId))
        {
            var participantExists = db.QueryScalar<long>("SELECT COUNT(*) FROM participants WHERE id = @p", ("@p", participantId)) > 0;
            if (!participantExists)
            {
                Context.Abort();
                return;
            }
        }
        Context.Items["participantId"] = participantId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"p:{participantId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, "arena");

        var canaryDeskId = config["RogueAgent:CanaryDeskId"] ?? "CANARY";
        var participantDesk = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", participantId));
        if (string.Equals(participantId, canaryDeskId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(participantDesk, canaryDeskId, StringComparison.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "canary");
        }
    }

    public Task JoinAdmin(string adminKey)
    {
        var expected = Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY") ?? config["Breach:AdminKey"] ?? "";
        var httpContext = Context.GetHttpContext();
        var remoteIp = httpContext?.Connection.RemoteIpAddress;
        var allowedIps = config.GetSection("Breach:AdminAllowedIps").Get<string[]>() ?? Array.Empty<string>();
        var ipStr = remoteIp?.ToString();
        var isAuthorized = remoteIp is null ||
                           System.Net.IPAddress.IsLoopback(remoteIp) ||
                           (remoteIp.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(remoteIp.MapToIPv4())) ||
                           ipStr == "::1" || ipStr == "127.0.0.1" || ipStr == "::ffff:127.0.0.1" ||
                           (ipStr != null && allowedIps.Contains(ipStr));

        var loopbackOnly = config.GetValue<bool>("Breach:AdminLoopbackOnly", true);

        if (loopbackOnly && !isAuthorized)
        {
            db.Log("HUB_ADMIN_DENIED_LAN", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\",\"ip\":\"{remoteIp}\"}}");
            return Task.CompletedTask;
        }

        var match = !string.IsNullOrEmpty(adminKey) && !string.IsNullOrEmpty(expected) &&
                    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(adminKey),
                        System.Text.Encoding.UTF8.GetBytes(expected));

        if (!match)
        {
            db.Log("HUB_ADMIN_AUTH_FAILED", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\"}}");
            return Task.CompletedTask;
        }
        return Groups.AddToGroupAsync(Context.ConnectionId, "admins");
    }

    public async Task JoinProjector()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "projectors");
        await Groups.AddToGroupAsync(Context.ConnectionId, "arena");
    }

    /// <summary>Kiosk confirms it flushed a final snapshot after BREACH_IMMINENT.</summary>
    public Task AckImminent(int attackId)
    {
        if (Context.Items.TryGetValue("participantId", out var pidObj) && pidObj is string pid)
        {
            return Clients.Group("admins").SendAsync("ack", attackId, pid);
        }
        return Task.CompletedTask;
    }

    /// <summary>Participant requests organizer authorization to switch their linked Google account.</summary>
    public async Task<string> RequestAccountSwitch(string currentEmail, string reason = "")
    {
        if (Context.Items.TryGetValue("participantId", out var pidObj) && pidObj is string pid)
        {
            var deskId = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", pid)) ?? pid;
            var participantName = db.QueryScalar<string>("SELECT name FROM participants WHERE id = @p", ("@p", pid)) ?? pid;
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var timestamp = DateTime.UtcNow.ToString("o");

            db.RecordComplianceAlert($"switch_{requestId}", pid, deskId, participantName, currentEmail, reason, "switch");

            db.Log("ACCOUNT_SWITCH_REQUESTED", pid, payloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                requestId,
                participantId = pid,
                deskId,
                name = participantName,
                email = currentEmail,
                reason,
                timestamp
            }));

            await Clients.Group("admins").SendAsync(HubEvents.AccountSwitchRequest, requestId, pid, deskId, participantName, currentEmail, reason, timestamp);
            return requestId;
        }
        return string.Empty;
    }

    /// <summary>Participant kiosk flags unauthorized Google AI Pro tier subscription detected in terminal stream.</summary>
    public async Task ReportProAccount(string currentEmail, string tierDetails)
    {
        if (Context.Items.TryGetValue("participantId", out var pidObj) && pidObj is string pid)
        {
            var deskId = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", pid)) ?? pid;
            var participantName = db.QueryScalar<string>("SELECT name FROM participants WHERE id = @p", ("@p", pid)) ?? pid;
            var timestamp = DateTime.UtcNow.ToString("o");

            db.RecordComplianceAlert($"pro_{pid}", pid, deskId, participantName, currentEmail, tierDetails, "pro");

            db.Log("PRO_ACCOUNT_FLAGGED", pid, payloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                participantId = pid,
                deskId,
                name = participantName,
                email = currentEmail,
                tier = tierDetails,
                timestamp
            }));

            await Clients.Group("admins").SendAsync(HubEvents.ProAccountFlagged, pid, deskId, participantName, currentEmail, tierDetails, timestamp);
        }
    }

    /// <summary>Participant kiosk flags focus switch, alt-tab, or app change tamper attempt.</summary>
    public async Task ReportFocusViolation(string reason, string? explicitPid = null)
    {
        string? pid = explicitPid;
        if (string.IsNullOrWhiteSpace(pid) && Context.Items.TryGetValue("participantId", out var pidObj) && pidObj is string ctxPid)
        {
            pid = ctxPid;
        }

        if (!string.IsNullOrWhiteSpace(pid))
        {
            var deskId = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", pid)) ?? pid;
            var participantName = db.QueryScalar<string>("SELECT name FROM participants WHERE id = @p", ("@p", pid)) ?? pid;
            var timestamp = DateTime.UtcNow.ToString("o");

            db.RecordComplianceAlert($"focus_{pid}", pid, deskId, participantName, reason, "Focus switch / Alt+Tab detected", "focus");
            db.Log("FOCUS_VIOLATION_FLAGGED", pid, payloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                participantId = pid,
                deskId,
                name = participantName,
                reason,
                timestamp
            }));

            await Clients.Group("admins").SendAsync(HubEvents.FocusViolation, pid, deskId, participantName, reason, timestamp);
            await Clients.All.SendAsync(HubEvents.FocusViolation, pid, deskId, participantName, reason, timestamp);
            await Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, pid);
        }
    }
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
    public const string ScoreboardFrozen = "FROZEN";
    public const string ScoreboardUnfrozen = "UNFROZEN";
    public const string EventFrozen = "EVENT_FROZEN";
    public const string EventUnfrozen = "EVENT_UNFROZEN";
    public const string KioskUpdated = "KIOSK_UPDATED";
    public const string AccountSwitchRequest = "ACCOUNT_SWITCH_REQUEST";
    public const string AccountSwitchApproved = "ACCOUNT_SWITCH_APPROVED";
    public const string AccountSwitchDenied = "ACCOUNT_SWITCH_DENIED";
    public const string ProAccountFlagged = "PRO_ACCOUNT_FLAGGED";
    public const string PardonGranted = "PARDON_GRANTED";
    public const string ProAccountAllowed = "PRO_ACCOUNT_ALLOWED";
    public const string Disqualified = "DISQUALIFIED";
    public const string ResetBaseline = "RESET_BASELINE";
    public const string FocusViolation = "FOCUS_VIOLATION";
    public const string FocusPardoned = "FOCUS_PARDONED";
}
