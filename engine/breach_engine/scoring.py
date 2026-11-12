"""Pure scoring functions (plan section 8). No I/O, so results are deterministic."""
from __future__ import annotations
from typing import Any

PENALTY_PER_FAILED_SUBMISSION = 3
PENALTY_CAP = 15


def clamp(x: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, x))


def speed_points(weight: int, seconds_to_solve: float, window_seconds: float) -> float:
    """Linear decay from landing to the end of the attack window."""
    if window_seconds <= 0:
        return 0.0
    return round(weight * clamp(1 - seconds_to_solve / window_seconds), 2)


def performance_points(weight: int, value: float, baseline: float, reference: float, lower_is_better: bool = True) -> float:
    """Normalise between the broken baseline (0) and the reference solution (1)."""
    if baseline == reference:
        return float(weight)
    frac = (baseline - value) / (baseline - reference) if lower_is_better else (value - baseline) / (reference - baseline)
    return round(weight * clamp(frac), 2)


def prompt_efficiency_points(weight: int, prompts_used: int, reference_prompts: int) -> float:
    """Full marks at or below reference, zero at 3x reference."""
    if reference_prompts <= 0:
        return float(weight)
    if prompts_used <= reference_prompts:
        return float(weight)
    frac = 1 - (prompts_used - reference_prompts) / (2 * reference_prompts)
    return round(weight * clamp(frac), 2)


def failed_submission_penalty(failed_count: int) -> int:
    return -min(PENALTY_CAP, PENALTY_PER_FAILED_SUBMISSION * max(0, failed_count))


def survival_bonus(total_seconds: float, seconds_with_active_attack: float, max_points: int = 100) -> float:
    if total_seconds <= 0:
        return 0.0
    return round(max_points * clamp(1 - seconds_with_active_attack / total_seconds), 2)


def calculate_benchmark_multiplier(metrics: dict[str, float], sla: dict[str, Any] | None = None) -> float:
    """Calculates a benchmark performance multiplier between 0.60 (naive fix) and 1.00 (optimal fix).

    sla spec example:
    {
        "target_p95_ms": 5.0,
        "max_p95_ms": 150.0,
        "target_ram_mb": 15.0,
        "max_ram_mb": 60.0,
        "floor": 0.60
    }
    """
    if not sla or not metrics:
        return 1.0

    floor = sla.get("floor", 0.60)
    scores = []

    # 1. Latency benchmark (p95)
    if "target_p95_ms" in sla and "latency_p95_ms" in metrics:
        target = float(sla["target_p95_ms"])
        ceiling = float(sla.get("max_p95_ms", target * 10))
        val = float(metrics["latency_p95_ms"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    # 2. RAM growth benchmark
    if "target_ram_mb" in sla and "peak_memory_mb" in metrics:
        target = float(sla["target_ram_mb"])
        ceiling = float(sla.get("max_ram_mb", target * 4))
        val = float(metrics["peak_memory_mb"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    # 3. CPU time benchmark
    if "target_cpu_ms" in sla and "cpu_time_ms" in metrics:
        target = float(sla["target_cpu_ms"])
        ceiling = float(sla.get("max_cpu_ms", target * 5))
        val = float(metrics["cpu_time_ms"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    if not scores:
        return 1.0

    return round(sum(scores) / len(scores), 4)

