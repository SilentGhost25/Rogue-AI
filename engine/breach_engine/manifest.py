"""Attack manifests. Shape is defined in contracts/attack-manifest.schema.json."""
from __future__ import annotations
from dataclasses import dataclass, field
from pathlib import Path
import yaml

REQUIRED = ("id", "name", "fires_at", "touches", "weights")
WEIGHT_KEYS = ("speed", "performance", "prompt_efficiency", "quality", "resource")


class ManifestError(ValueError):
    pass


@dataclass
class Attack:
    id: int
    name: str
    fires_at: str
    touches: list[str]
    weights: dict[str, int]
    fix_requires: str = "Diagnose system logs and telemetry to remediate incident invariants."
    replay_checks: list[str] = field(default_factory=list)
    hidden_variant_checks: list[str] = field(default_factory=list)
    reference_prompts: int = 0
    directory: Path | None = None


def load_manifest(directory: str | Path) -> Attack:
    directory = Path(directory)
    data = yaml.safe_load((directory / "manifest.yaml").read_text(encoding="utf-8"))
    missing = [k for k in REQUIRED if k not in data]
    if missing:
        raise ManifestError(f"{directory.name}: missing keys {missing}")
    weights = data["weights"]
    if set(weights) != set(WEIGHT_KEYS):
        raise ManifestError(f"{directory.name}: weights must have exactly {WEIGHT_KEYS}")
    if sum(weights.values()) != 100:
        raise ManifestError(f"{directory.name}: weights must sum to 100")
    return Attack(
        id=int(data["id"]), name=data["name"], fires_at=str(data["fires_at"]),
        touches=list(data["touches"]),
        weights=dict(weights),
        fix_requires=str(data.get("fix_requires", "Diagnose system logs and telemetry to remediate incident invariants.")),
        replay_checks=list(data.get("replay_checks", [])),
        hidden_variant_checks=list(data.get("hidden_variant_checks", [])),
        reference_prompts=int(data.get("reference_prompts", 0)), directory=directory,
    )


def load_all(attacks_root: str | Path) -> list[Attack]:
    dirs = sorted(p for p in Path(attacks_root).iterdir() if (p / "manifest.yaml").exists())
    return [load_manifest(d) for d in dirs]
