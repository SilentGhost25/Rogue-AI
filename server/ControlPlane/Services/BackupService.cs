using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Services;

public record BackupPruneResult(
    List<string> DeletedRollingFiles,
    List<string> RetainedRollingFiles,
    List<string> RetainedHourlyFiles
);

public record BackupStatus(
    string? LastBackupUtc,
    double BackupAgeMinutes,
    int BackupCount,
    bool IsWarning,
    bool MirrorAvailable
);

public sealed class BackupService : BackgroundService
{
    private readonly string _connectionString;
    private readonly string _backupDir;
    private readonly string? _mirrorDir;
    private readonly ILogger<BackupService>? _logger;
    private readonly TimeSpan _interval;
    private string? _lastHourlyBucket;

    public BackupService(IConfiguration config, ILogger<BackupService>? logger = null)
    {
        var dbPath = config["Breach:DatabasePath"] ?? "data/breach.db";
        _connectionString = $"Data Source={dbPath}";
        _backupDir = config["Breach:BackupPath"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups");
        _mirrorDir = config["Breach:BackupMirrorPath"];
        _logger = logger;
        _interval = TimeSpan.FromMinutes(3);
    }

    public static string GenerateFileName(DateTime timestamp, bool isHourly)
    {
        var tsStr = timestamp.ToUniversalTime().ToString("yyyyMMdd_HHmmss'Z'", CultureInfo.InvariantCulture);
        return isHourly ? $"breach_{tsStr}_hourly.db" : $"breach_{tsStr}.db";
    }

    public static bool IsHourlySnapshot(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.EndsWith("_hourly", StringComparison.OrdinalIgnoreCase);
    }

    public static BackupStatus GetBackupStatus(string backupDir, string? mirrorDir)
    {
        if (!Directory.Exists(backupDir))
        {
            return new BackupStatus(null, 999.0, 0, true, false);
        }
        var files = Directory.GetFiles(backupDir, "breach_*.db");
        if (files.Length == 0)
        {
            return new BackupStatus(null, 999.0, 0, true, false);
        }
        var newestFile = files.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var lastWriteUtc = File.GetLastWriteTimeUtc(newestFile);
        var age = (DateTime.UtcNow - lastWriteUtc).TotalMinutes;
        var mirrorAvailable = !string.IsNullOrEmpty(mirrorDir) && Directory.Exists(Path.GetPathRoot(mirrorDir));
        return new BackupStatus(lastWriteUtc.ToString("O"), Math.Round(age, 1), files.Length, age > 10.0, mirrorAvailable);
    }

    public static BackupPruneResult PruneBackups(string backupDir, int maxRolling = 20)
    {
        if (!Directory.Exists(backupDir))
        {
            return new BackupPruneResult(new(), new(), new());
        }

        var allFiles = Directory.GetFiles(backupDir, "breach_*.db");
        var hourlyFiles = new List<string>();
        var rollingFiles = new List<string>();

        foreach (var file in allFiles)
        {
            if (IsHourlySnapshot(file))
            {
                hourlyFiles.Add(file);
            }
            else
            {
                rollingFiles.Add(file);
            }
        }

        // Sort rolling files by filename ascending (which matches yyyyMMdd_HHmmss ordering)
        rollingFiles.Sort(StringComparer.OrdinalIgnoreCase);

        var deleted = new List<string>();
        var retainedRolling = new List<string>();

        if (rollingFiles.Count > maxRolling)
        {
            var deleteCount = rollingFiles.Count - maxRolling;
            for (var i = 0; i < deleteCount; i++)
            {
                try
                {
                    File.Delete(rollingFiles[i]);
                    deleted.Add(rollingFiles[i]);
                }
                catch
                {
                    // If file is locked, retain it for next prune cycle
                    retainedRolling.Add(rollingFiles[i]);
                }
            }
            for (var i = deleteCount; i < rollingFiles.Count; i++)
            {
                retainedRolling.Add(rollingFiles[i]);
            }
        }
        else
        {
            retainedRolling.AddRange(rollingFiles);
        }

        return new BackupPruneResult(deleted, retainedRolling, hourlyFiles);
    }

    public string CreateSnapshot(DateTime timestamp, bool isHourly)
    {
        Directory.CreateDirectory(_backupDir);
        var fileName = GenerateFileName(timestamp, isHourly);
        var targetPath = Path.Combine(_backupDir, fileName);

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // SQLite VACUUM INTO creates a safe, isolated transaction snapshot
        cmd.CommandText = "VACUUM INTO $target;";
        cmd.Parameters.AddWithValue("$target", targetPath);
        cmd.ExecuteNonQuery();

        return targetPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("BackupService background worker started. Snapshots every {Interval} min.", _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var currentHourBucket = now.ToString("yyyyMMdd_HH", CultureInfo.InvariantCulture);
                var isHourly = false;

                if (_lastHourlyBucket != currentHourBucket)
                {
                    var existingHourly = Directory.Exists(_backupDir) && Directory.GetFiles(_backupDir, $"breach_{currentHourBucket}*_hourly.db").Length > 0;
                    if (!existingHourly)
                    {
                        isHourly = true;
                    }
                    _lastHourlyBucket = currentHourBucket;
                }

                var snapshotPath = CreateSnapshot(now, isHourly);
                _logger?.LogInformation("Created database snapshot: {Path} (Hourly: {IsHourly})", snapshotPath, isHourly);

                var pruneResult = PruneBackups(_backupDir, maxRolling: 20);
                if (pruneResult.DeletedRollingFiles.Count > 0)
                {
                    _logger?.LogInformation("Pruned {Count} rolling snapshots. Retained {Rolling} rolling and {Hourly} hourly snapshots.",
                        pruneResult.DeletedRollingFiles.Count, pruneResult.RetainedRollingFiles.Count, pruneResult.RetainedHourlyFiles.Count);
                }

                if (!string.IsNullOrEmpty(_mirrorDir) && Directory.Exists(Path.GetPathRoot(_mirrorDir)))
                {
                    try
                    {
                        Directory.CreateDirectory(_mirrorDir);
                        var mirrorTarget = Path.Combine(_mirrorDir, Path.GetFileName(snapshotPath));
                        File.Copy(snapshotPath, mirrorTarget, overwrite: true);
                        PruneBackups(_mirrorDir, maxRolling: 20);
                        _logger?.LogInformation("Mirrored database snapshot to external target: {Path}", mirrorTarget);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not mirror backup snapshot to external mirror: {Dir}", _mirrorDir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create or prune database backup.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
