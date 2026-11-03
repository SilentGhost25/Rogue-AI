using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ControlPlane.Models;

public sealed class AttackManifest
{
    [YamlMember(Alias = "id")]
    public int Id { get; set; }

    [YamlMember(Alias = "title")]
    public string Title { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "codename")]
    public string Codename { get; set; } = "";

    [YamlMember(Alias = "band")]
    public int Band { get; set; } = 1;

    [YamlMember(Alias = "dispatch")]
    public AttackDispatch Dispatch { get; set; } = new();

    [YamlMember(Alias = "scoring")]
    public AttackScoring Scoring { get; set; } = new();

    [YamlMember(Alias = "payload")]
    public AttackPayload Payload { get; set; } = new();

    [YamlMember(Alias = "tests")]
    public AttackTests Tests { get; set; } = new();

    [YamlMember(Alias = "hints")]
    public AttackHints Hints { get; set; } = new();

    [YamlMember(Alias = "narrative")]
    public AttackNarrative Narrative { get; set; } = new();

    [YamlMember(Alias = "touches")]
    public List<string> Touches { get; set; } = new();

    [YamlMember(Alias = "fires_at")]
    public string FiresAt { get; set; } = "";

    public TimeOnly ScheduledTime => TimeOnly.Parse(Dispatch.Time);

    public DateTime ScheduledTimeToday(DateTime arenaNowLocal)
    {
        var local = DateTime.SpecifyKind(
            arenaNowLocal.Date.Add(ScheduledTime.ToTimeSpan()),
            DateTimeKind.Local);
        return local.ToUniversalTime();
    }

    public static AttackManifest LoadFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var manifest = deserializer.Deserialize<AttackManifest>(yaml);
        if (string.IsNullOrWhiteSpace(manifest.Title) && !string.IsNullOrWhiteSpace(manifest.Name))
        {
            manifest.Title = manifest.Name;
        }
        return manifest;
    }

    public void Validate(string attacksRoot)
    {
        if (Id < 0) throw new InvalidDataException($"Attack id must be >= 0 (got {Id})");
        if (Dispatch.ImminentSeconds < 3)
            throw new InvalidDataException($"Attack #{Id}: imminent_seconds must be >= 3");
        if (string.IsNullOrWhiteSpace(Payload.Path))
            throw new InvalidDataException($"Attack #{Id}: payload.path missing");

        var payloadDir = Path.Combine(attacksRoot, Payload.Path);
        if (!Directory.Exists(payloadDir))
            throw new InvalidDataException($"Attack #{Id}: payload dir not found at {payloadDir}");

        var testsDir = Path.Combine(attacksRoot, Tests.Path);
        if (!Directory.Exists(testsDir))
            throw new InvalidDataException($"Attack #{Id}: tests dir not found at {testsDir}");

        if (Tests.Tiers.Count == 0 || Tests.Tiers.Sum(t => t.Weight) != 100)
            throw new InvalidDataException($"Attack #{Id}: tier weights must sum to 100");

        var payloadBytes = new DirectoryInfo(payloadDir)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
        if (payloadBytes > 2 * 1024 * 1024)
            throw new InvalidDataException($"Attack #{Id}: payload exceeds 2MB budget ({payloadBytes} bytes)");
    }
}

public sealed class AttackDispatch
{
    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "manual";   // manual | scheduled | canary_only

    [YamlMember(Alias = "time")]
    public string Time { get; set; } = "09:00:00"; // arena-local HH:mm:ss

    [YamlMember(Alias = "imminent_seconds")]
    public int ImminentSeconds { get; set; } = 10;
}

public sealed class AttackScoring
{
    [YamlMember(Alias = "base_points")]
    public long BasePoints { get; set; } = 8500;

    [YamlMember(Alias = "speed_bonus_max")]
    public long SpeedBonusMax { get; set; } = 1500;

    [YamlMember(Alias = "speed_bonus_window_seconds")]
    public int SpeedBonusWindowSeconds { get; set; } = 1800;

    [YamlMember(Alias = "late_solve_multiplier")]
    public double LateSolveMultiplier { get; set; } = 0.70;
}

public sealed class AttackPayload
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "payload/";

    [YamlMember(Alias = "conflict_files")]
    public List<string> ConflictFiles { get; set; } = new();

    [YamlMember(Alias = "format")]
    public string Format { get; set; } = "full";   // full | patch
}

public sealed class AttackTests
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "tests/";

    [YamlMember(Alias = "tiers")]
    public List<TestTier> Tiers { get; set; } = new();
}

public sealed class TestTier
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "weight")]
    public int Weight { get; set; }
}

public sealed class AttackHints
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "hints.yaml";

    [YamlMember(Alias = "tiers")]
    public List<HintTier> Tiers { get; set; } = new();
}

public sealed class HintTier
{
    [YamlMember(Alias = "level")]
    public int Level { get; set; }

    [YamlMember(Alias = "cost_centipoints")]
    public int CostCentipoints { get; set; }

    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "";
}

public sealed class AttackNarrative
{
    [YamlMember(Alias = "landed")]
    public string Landed { get; set; } = "";

    [YamlMember(Alias = "broadcast_title")]
    public string BroadcastTitle { get; set; } = "";

    [YamlMember(Alias = "solve_banner")]
    public string SolveBanner { get; set; } = "";
}
