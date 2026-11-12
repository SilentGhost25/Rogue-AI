using System.Security.Cryptography;
using ControlPlane.Data;
using ControlPlane.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class EvaluationDiffGateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempWs;
    private readonly Database _db;
    private readonly WorkspaceStore _store;

    public EvaluationDiffGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"diffgate_test_{Guid.NewGuid():N}.db");
        _tempWs = Path.Combine(Path.GetTempPath(), $"diffgate_ws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWs);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Breach:DatabasePath"] = _dbPath,
                ["Breach:WorkspacesPath"] = _tempWs
            })
            .Build();

        _db = new Database(config);
        _store = new WorkspaceStore(config);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_tempWs)) Directory.Delete(_tempWs, true); } catch { }
    }

    [Fact]
    public void ApplyAttackPayload_And_DiffGate_DetectsZeroModifications()
    {
        var pid = "p_diff_test1";
        var attackId = 2;

        // 1. Prepare an attack payload
        var attackContent = "module.exports = { defect: true };";
        var attackBytes = System.Text.Encoding.UTF8.GetBytes(attackContent);
        var attackHash = Convert.ToHexString(SHA256.HashData(attackBytes));

        var files = new List<SnapshotFile>
        {
            new("routes/overview.js", attackHash, Convert.ToBase64String(attackBytes))
        };

        // 2. Server applies attack payload
        var written = _store.ApplyAttackPayload(pid, attackId, files);
        Assert.Single(written);
        Assert.Equal("routes/overview.js", written[0].Path);
        Assert.Equal(attackHash, written[0].Sha256);

        _db.RecordAttackBaseline(pid, attackId, written[0].Path, written[0].Sha256);

        // 3. Query baseline
        var baselines = _db.GetAttackBaselines(pid, attackId);
        Assert.Single(baselines);
        Assert.Equal("routes/overview.js", baselines[0].RelPath);
        Assert.Equal(attackHash, baselines[0].BaselineHash);

        // 4. Verify Diff Gate: without edits, file matches baseline exactly (zero modifications)
        var fullPath = _store.ResolveSafe(pid, "routes/overview.js");
        Assert.NotNull(fullPath);
        Assert.True(File.Exists(fullPath));

        var currentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
        Assert.Equal(attackHash, currentHash);
    }

    [Fact]
    public void DiffGate_DetectsParticipantCodeModification()
    {
        var pid = "p_diff_test2";
        var attackId = 2;

        var attackContent = "module.exports = { defect: true };";
        var attackBytes = System.Text.Encoding.UTF8.GetBytes(attackContent);
        var attackHash = Convert.ToHexString(SHA256.HashData(attackBytes));

        var files = new List<SnapshotFile>
        {
            new("routes/overview.js", attackHash, Convert.ToBase64String(attackBytes))
        };

        _store.ApplyAttackPayload(pid, attackId, files);
        _db.RecordAttackBaseline(pid, attackId, "routes/overview.js", attackHash);

        // Simulate participant fixing the code
        var fixedContent = "module.exports = { defect: false, sanitized: true };";
        var fixedBytes = System.Text.Encoding.UTF8.GetBytes(fixedContent);
        var fixedHash = Convert.ToHexString(SHA256.HashData(fixedBytes));

        _store.Save(pid, new List<SnapshotFile>
        {
            new("routes/overview.js", fixedHash, Convert.ToBase64String(fixedBytes))
        });

        // Verify Diff Gate detects modification
        var baselines = _db.GetAttackBaselines(pid, attackId);
        var fullPath = _store.ResolveSafe(pid, "routes/overview.js");
        Assert.NotNull(fullPath);

        var currentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
        Assert.NotEqual(baselines[0].BaselineHash, currentHash);
    }
}

