using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace ControlPlane.Models;

public class AttackManifest
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
    public DispatchConfig Dispatch { get; set; } = new();

    [YamlMember(Alias = "scoring")]
    public ScoringConfig Scoring { get; set; } = new();

    [YamlMember(Alias = "payload")]
    public PayloadConfig Payload { get; set; } = new();

    [YamlMember(Alias = "tests")]
    public TestsConfig Tests { get; set; } = new();

    [YamlMember(Alias = "hints")]
    public HintsConfig Hints { get; set; } = new();

    [YamlMember(Alias = "narrative")]
    public NarrativeConfig Narrative { get; set; } = new();

    [YamlMember(Alias = "touches")]
    public List<string> Touches { get; set; } = new();

    [YamlMember(Alias = "fires_at")]
    public string FiresAt { get; set; } = "";
}

public class DispatchConfig
{
    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "scheduled"; // scheduled | manual | canary_only

    [YamlMember(Alias = "time")]
    public string Time { get; set; } = "10:00:00";

    [YamlMember(Alias = "imminent_seconds")]
    public int ImminentSeconds { get; set; } = 10;
}

public class ScoringConfig
{
    [YamlMember(Alias = "base_points")]
    public int BasePoints { get; set; } = 8500;

    [YamlMember(Alias = "speed_bonus_max")]
    public int SpeedBonusMax { get; set; } = 1500;

    [YamlMember(Alias = "speed_bonus_window_seconds")]
    public int SpeedBonusWindowSeconds { get; set; } = 1800;

    [YamlMember(Alias = "late_solve_multiplier")]
    public double LateSolveMultiplier { get; set; } = 0.70;
}

public class PayloadConfig
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "payload/";

    [YamlMember(Alias = "conflict_files")]
    public List<string> ConflictFiles { get; set; } = new();
}

public class TestsConfig
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "tests/";

    [YamlMember(Alias = "tiers")]
    public List<TierConfig> Tiers { get; set; } = new();
}

public class TierConfig
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "weight")]
    public int Weight { get; set; } = 0;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";
}

public class HintsConfig
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "hints.yaml";

    [YamlMember(Alias = "tiers")]
    public List<HintTier> Tiers { get; set; } = new();
}

public class HintTier
{
    [YamlMember(Alias = "level")]
    public int Level { get; set; }

    [YamlMember(Alias = "cost_centipoints")]
    public int CostCentipoints { get; set; }

    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "";
}

public class NarrativeConfig
{
    [YamlMember(Alias = "landed")]
    public string Landed { get; set; } = "";

    [YamlMember(Alias = "broadcast_title")]
    public string BroadcastTitle { get; set; } = "";

    [YamlMember(Alias = "solve_banner")]
    public string SolveBanner { get; set; } = "";
}
