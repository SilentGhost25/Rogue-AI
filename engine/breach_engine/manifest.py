"""Attack manifests. Shape is defined in contracts/attack-manifest.schema.json."""
from __future__ import annotations
from dataclasses import dataclass, field
from pathlib import Path
import yaml

REQUIRED = ("id", "name", "fires_at", "touches", "fix_requires")


class ManifestError(ValueError):
    pass


@dataclass
class Attack:
    id: int
    name: str
    fires_at: str
    touches: list[str]
    fix_requires: str
    window_minutes: int = 45
    prompt_threshold: int = 8
    benchmark: dict = field(default_factory=dict)
    directory: Path | None = None


def load_manifest(directory: str | Path) -> Attack:
    directory = Path(directory)
    data = yaml.safe_load((directory / "manifest.yaml").read_text(encoding="utf-8"))
    missing = [k for k in REQUIRED if k not in data]
    if missing:
        raise ManifestError(f"{directory.name}: missing keys {missing}")
    if not data["touches"]:
        raise ManifestError(f"{directory.name}: touches must not be empty")
    return Attack(
        id=int(data["id"]), name=data["name"], fires_at=str(data["fires_at"]), touches=list(data["touches"]),
        fix_requires=data["fix_requires"], window_minutes=int(data.get("window_minutes", 45)),
        prompt_threshold=int(data.get("prompt_threshold", 8)), benchmark=dict(data.get("benchmark", {})), directory=directory,
    )


def load_all(attacks_root: str | Path) -> list[Attack]:
    dirs = sorted(p for p in Path(attacks_root).iterdir() if (p / "manifest.yaml").exists())
    return [load_manifest(d) for d in dirs]
