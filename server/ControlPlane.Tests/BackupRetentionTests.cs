using ControlPlane.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ControlPlane.Tests;

public class BackupRetentionTests : IDisposable
{
    private readonly string _testDir;

    public BackupRetentionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "breach_backup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void Pruning_never_touches_hourly_snapshots_and_retains_20_rolling()
    {
        // Simulate a 7-hour competition scenario:
        // 7 hourly snapshots: hours 09 to 15
        // 40 rolling snapshots: taken every few minutes
        var baseTime = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        var expectedHourlyFiles = new List<string>();
        var allRollingFiles = new List<string>();

        // Create 7 hourly snapshots
        for (int h = 0; h < 7; h++)
        {
            var t = baseTime.AddHours(h);
            var hourlyName = BackupService.GenerateFileName(t, isHourly: true);
            var fullPath = Path.Combine(_testDir, hourlyName);
            File.WriteAllText(fullPath, "sqlite-dummy-hourly-data-" + h);
            expectedHourlyFiles.Add(fullPath);
        }

        // Create 40 rolling snapshots spread across the hours
        for (int i = 1; i <= 40; i++)
        {
            var t = baseTime.AddMinutes(i * 10);
            var rollingName = BackupService.GenerateFileName(t, isHourly: false);
            var fullPath = Path.Combine(_testDir, rollingName);
            File.WriteAllText(fullPath, "sqlite-dummy-rolling-data-" + i);
            allRollingFiles.Add(fullPath);
        }

        // Total created: 47 files (7 hourly + 40 rolling)
        Assert.Equal(47, Directory.GetFiles(_testDir, "*.db").Length);

        // Execute pruning with maxRolling = 20
        var result = BackupService.PruneBackups(_testDir, maxRolling: 20);

        // 1. Verify 20 oldest rolling files were deleted
        Assert.Equal(20, result.DeletedRollingFiles.Count);
        Assert.Equal(20, result.RetainedRollingFiles.Count);

        // 2. Verify all 7 hourly snapshots were retained completely
        Assert.Equal(7, result.RetainedHourlyFiles.Count);
        foreach (var hourly in expectedHourlyFiles)
        {
            Assert.True(File.Exists(hourly), $"Hourly backup was unexpectedly deleted: {hourly}");
            Assert.Contains(hourly, result.RetainedHourlyFiles);
        }

        // 3. Verify exactly the 20 newest rolling files were kept
        var expectedRetainedRolling = allRollingFiles.Skip(20).ToList();
        foreach (var expectedKept in expectedRetainedRolling)
        {
            Assert.True(File.Exists(expectedKept), $"Expected rolling backup to be kept: {expectedKept}");
        }

        // 4. Verify the 20 oldest rolling files were physically removed from disk
        var expectedDeletedRolling = allRollingFiles.Take(20).ToList();
        foreach (var expectedDel in expectedDeletedRolling)
        {
            Assert.False(File.Exists(expectedDel), $"Old rolling backup should have been deleted: {expectedDel}");
        }

        // 5. Total remaining files on disk: 7 hourly + 20 rolling = 27
        var remainingFiles = Directory.GetFiles(_testDir, "*.db");
        Assert.Equal(27, remainingFiles.Length);
    }

    [Fact]
    public void Pruning_does_not_delete_anything_when_rolling_count_is_below_limit()
    {
        var baseTime = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 10; i++)
        {
            var t = baseTime.AddMinutes(i * 3);
            var name = BackupService.GenerateFileName(t, isHourly: false);
            File.WriteAllText(Path.Combine(_testDir, name), "data");
        }

        var result = BackupService.PruneBackups(_testDir, maxRolling: 20);
        Assert.Empty(result.DeletedRollingFiles);
        Assert.Equal(10, result.RetainedRollingFiles.Count);
    }

    [Fact]
    public void IsHourlySnapshot_identifies_hourly_files_correctly()
    {
        Assert.True(BackupService.IsHourlySnapshot("breach_20261113_090000_hourly.db"));
        Assert.True(BackupService.IsHourlySnapshot(@"C:\data\backups\breach_20261113_140000_hourly.db"));
        Assert.False(BackupService.IsHourlySnapshot("breach_20261113_090300.db"));
        Assert.False(BackupService.IsHourlySnapshot(@"C:\data\backups\breach_20261113_140300.db"));
    }
}
