"""Pure scoring functions (plan revision 3, section 10). No I/O, so results are deterministic."""
from __future__ import annotations

BASE_SOLVE_POINTS = 85.0
TIME_BONUS_MAX = 15.0
FAILED_SUBMISSION_PENALTY = -3.0
PROMPT_PENALTY_EACH = 0.2


def clamp(x: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, x))


def time_bonus(elapsed_seconds: float, window_seconds: float) -> float:
    """Up to +15, scaled inversely against time-to-fix (linear over the attack window)."""
    if window_seconds <= 0:
        return 0.0
    return round(TIME_BONUS_MAX * clamp(1 - elapsed_seconds / window_seconds), 2)


def prompt_penalty(prompts_used: int, threshold: int) -> float:
    """-0.2 per OpenCode prompt beyond the threshold (always zero or negative)."""
    return round(-PROMPT_PENALTY_EACH * max(0, prompts_used - max(0, threshold)), 2)


def solve_components(elapsed_seconds: float, window_seconds: float, prompts_used: int, threshold: int) -> dict[str, float]:
    return {
        "base": BASE_SOLVE_POINTS,
        "time_bonus": time_bonus(elapsed_seconds, window_seconds),
        "prompt_penalty": prompt_penalty(prompts_used, threshold),
    }
