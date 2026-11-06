using ControlPlane.Data;

namespace ControlPlane.Services;

/// <summary>Fires attacks at their scheduled time on the pause-adjusted event clock. Times are editable data.</summary>
public sealed class SchedulerService(Database db, EventState state, AttackDispatcher dispatcher, ILogger<SchedulerService> log) : BackgroundService
{
    // Name and default time of each attack; admins can edit times via PUT /admin/attacks/{n}/schedule.
    private static readonly (int Id, string Name, string At)[] Defaults =
    {
        (1, "Defacement and Route Redirect", "10:45"), (2, "Stored Cross-Site Scripting (XSS)", "11:30"),
        (3, "SQL Injection Extraction", "12:15"), (4, "Hardcoded Admin Backdoor", "13:00"),
        (5, "Auth Bypass and Permissive CORS", "14:30"), (6, "CPU Performance Sabotage", "15:30"),
        (7, "Poisoned Dependency Hook", "16:30"), (8, "Broken Auth Cookie Tampering", "17:00"),
        (9, "Parameter Pollution & Mass Assignment", "17:30"), (10, "Static File Path Traversal", "18:00")
    };

    public static void Seed(Database db)
    {
        foreach (var (id, name, at) in Defaults)
            db.Exec("INSERT OR IGNORE INTO attacks (id, name, fires_at) VALUES ($i,$n,$a)", ("$i", id), ("$n", name), ("$a", at));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Seed(db);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { return; }
            if (state.Paused) continue;
            var now = state.ScheduleNow;
            var due = db.Query("SELECT id, fires_at FROM attacks WHERE state = 'scheduled' ORDER BY id", r => (Id: r.GetInt32(0), At: r.GetString(1)));
            foreach (var (id, at) in due)
            {
                if (!TimeOnly.TryParse(at, out var t)) continue;
                var scheduled = now.Date + t.ToTimeSpan();
                // Only fire in a 10 minute window, so a server restart mid-event never replays earlier attacks.
                if (now >= scheduled && now - scheduled < TimeSpan.FromMinutes(10))
                {
                    log.LogInformation("Scheduler firing attack {Attack}", id);
                    _ = Task.Run(() => dispatcher.FireAsync(id, null, ct), ct);
                }
            }
        }
    }
}
