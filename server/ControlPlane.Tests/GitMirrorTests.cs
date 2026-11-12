using System.Diagnostics;
using ControlPlane.Data;
using ControlPlane.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public sealed class GitMirrorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly Database _db;

    public GitMirrorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "git_mirror_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "test.db");
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Breach:DatabasePath", _dbPath },
            { "Breach:GitMirrorUrl", "https://github.com/Prince649294u83/Rogue-AI-competition.git" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _db = new Database(config);
    }

    [Fact]
    public async Task SyncBranchAsync_InitializesGit_AndCommitsFiles()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "server.js"), "console.log('baseline');");
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Breach:DatabasePath", _dbPath },
            { "Breach:GitMirrorUrl", "https://github.com/Prince649294u83/Rogue-AI-competition.git" },
            { "Breach:GitHubToken", "" } // Local commit only, no remote push
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var mirror = new GitMirrorService(config, _db);

        // Act
        var result = await mirror.SyncBranchAsync("ptest", _tempDir, "Baseline snapshot commit");

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, ".git")));
        Assert.True(File.Exists(Path.Combine(_tempDir, ".gitignore")));

        // Verify git log output contains our commit message
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "log -1 --pretty=%B",
                WorkingDirectory = _tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        var log = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        Assert.Contains("Baseline snapshot commit", log);
    }

    public void Dispose()
    {
        try
        {
            // Set normal attributes recursively so .git read-only objects can be deleted
            if (Directory.Exists(_tempDir))
            {
                foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}

