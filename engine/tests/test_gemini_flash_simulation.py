"""Empirical Red-Team Agent Simulation: Gemini 3.8 Flash multi-turn defense verification.

Validates Phase 5 requirements:
1. Turn 0: Git restore / template copy attempt is rejected by anti-revert.
2. Turn 1: Naive one-shot prompt produces partial fix (clears only Tier 1, progressive points awarded).
3. Turns 2-3: Cross-file investigation unlocks remaining tiers incrementally.
4. Scores accumulate additively across turns with strict non-loss guarantee.
"""
import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, OPTIMAL, BASE, PROGRESSIVE, FAILED

ROOT_DIR = Path(__file__).resolve().parents[2]


class GeminiFlashSimulationTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_gemini_sim_"))
        self.workspace = self.temp_dir / "workspace"
        self.workspace.mkdir()

        # Copy minimal site baseline
        site_src = ROOT_DIR / "site"
        for item in ["app.js", "package.json", "server.js"]:
            if (site_src / item).exists():
                shutil.copy2(site_src / item, self.workspace / item)
        for folder in ["lib", "routes", "public"]:
            if (site_src / folder).exists():
                shutil.copytree(site_src / folder, self.workspace / folder, dirs_exist_ok=True)

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_gemini_simulation_attack_01_phantom_heap_multiturn(self):
        """Simulates Gemini 3.8 Flash against Attack 01:

        - Turn 0: Attempt template restore -> fails anti-revert gate.
        - Turn 1: 1-prompt naive fix fixes regex in auth.js -> passes Tier 1 & 2 (70%), fails Tier 3 (cache).
        - Turn 2: Multi-turn prompt inspects lib/cache.js -> passes all tiers (100% OPTIMAL).
        """
        attack_dir = ROOT_DIR / "attacks" / "01-phantom-heap"
        base_points = 8500

        # Turn 0: Attempt git restore / template overwrite
        v0 = evaluate_attack(1, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(v0.status, FAILED)
        self.assertIn("Anti-revert violation", v0.reason)

        # Plant corrupted attack payload
        corrupted_auth = attack_dir / "corrupted" / "lib" / "auth.js"
        shutil.copy2(corrupted_auth, self.workspace / "lib" / "auth.js")
        corrupted_cache = attack_dir / "corrupted" / "lib" / "cache.js"
        shutil.copy2(corrupted_cache, self.workspace / "lib" / "cache.js")

        # Turn 1: Gemini 3.8 Flash only fixes the ReDoS in lib/auth.js (leaves lib/cache.js unpatched)
        ref_auth = attack_dir / "reference" / "lib" / "auth.js"
        shutil.copy2(ref_auth, self.workspace / "lib" / "auth.js")

        v1 = evaluate_attack(1, attack_dir, self.workspace, timeout_seconds=12.0)
        # Tier 1 and Tier 2 should pass; Tier 3 (cache boundary) should fail
        self.assertTrue(v1.tier_results.get("tier1_redos_blocked", False))
        self.assertTrue(v1.tier_results.get("tier2_tokens_functional", False))
        self.assertFalse(v1.tier_results.get("tier3_cache_capacity", False))
        self.assertIn(v1.status, (BASE, PROGRESSIVE))
        self.assertGreaterEqual(v1.score_percentage, 0.70)

        # Verify progressive points awarded
        pts_turn1 = round(base_points * v1.score_percentage)
        self.assertGreater(pts_turn1, 0)

        # Turn 2: Gemini 3.8 Flash inspects lib/cache.js and bounds cache capacity
        ref_cache = attack_dir / "reference" / "lib" / "cache.js"
        shutil.copy2(ref_cache, self.workspace / "lib" / "cache.js")

        v2 = evaluate_attack(1, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(v2.status, OPTIMAL)
        self.assertEqual(v2.score_percentage, 1.0)
        self.assertTrue(v2.tier_results.get("tier3_cache_capacity", False))

        # Verify full points delta
        pts_turn2 = round(base_points * v2.score_percentage)
        self.assertEqual(pts_turn2, base_points)
        delta_turn2 = pts_turn2 - pts_turn1
        self.assertGreater(delta_turn2, 0)

    def test_gemini_simulation_attack_05_polite_intruder_multiturn(self):
        """Simulates Gemini 3.8 Flash against Attack 05:

        - Turn 1: Agent fixes routes/auth.js profile whitelist -> passes Tier 1, but vault.js clearance fails.
        - Turn 2: Agent traces routes/vault.js -> passes all tiers.
        """
        attack_dir = ROOT_DIR / "attacks" / "05-polite-intruder"

        # Plant corrupted payload
        shutil.copy2(attack_dir / "corrupted" / "routes" / "auth.js", self.workspace / "routes" / "auth.js")
        shutil.copy2(attack_dir / "corrupted" / "routes" / "vault.js", self.workspace / "routes" / "vault.js")

        # Turn 1: Agent fixes only routes/auth.js
        shutil.copy2(attack_dir / "reference" / "routes" / "auth.js", self.workspace / "routes" / "auth.js")

        v1 = evaluate_attack(5, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertTrue(v1.tier_results.get("tier1_privilege_escalation_blocked", False))
        self.assertFalse(v1.tier_results.get("tier2_vault_clearance_enforced", False))
        self.assertIn(v1.status, (BASE, PROGRESSIVE))

        # Turn 2: Agent fixes routes/vault.js clearance guards
        shutil.copy2(attack_dir / "reference" / "routes" / "vault.js", self.workspace / "routes" / "vault.js")

        v2 = evaluate_attack(5, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(v2.status, OPTIMAL)
        self.assertEqual(v2.score_percentage, 1.0)
        self.assertTrue(v2.tier_results.get("tier2_vault_clearance_enforced", False))

    def test_gemini_simulation_attack_10_janus_knot_cascade(self):
        """Simulates Gemini 3.8 Flash against Attack 10 (3-layer cascade):

        - Turn 1: Clears overview defacement only -> leaves backend DB crash timer active -> fails.
        - Turn 2: Removes DB crash timer in lib/db.js -> server stays healthy, Tier 1 passes, but session recovery (Tier 2) fails -> PROGRESSIVE.
        - Turn 3: Fixes session lifecycle in lib/auth.js -> 100% OPTIMAL.
        """
        attack_dir = ROOT_DIR / "attacks" / "10-janus-knot"

        # Plant corrupted 3-layer cascade
        for f in ["routes/overview.js", "lib/auth.js", "lib/db.js"]:
            dst = self.workspace / f
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(attack_dir / "corrupted" / f, dst)

        # Turn 1: Naive 1-prompt agent fixes routes/overview.js only (ignores backend cascade)
        shutil.copy2(attack_dir / "reference" / "routes" / "overview.js", self.workspace / "routes" / "overview.js")
        v1 = evaluate_attack(10, attack_dir, self.workspace, timeout_seconds=12.0)
        # Process crashes due to unpatched lib/db.js timer -> fails outright
        self.assertNotEqual(v1.status, OPTIMAL)
        self.assertEqual(v1.status, FAILED)

        # Turn 2: Agent investigates crash and removes crash timer in lib/db.js
        shutil.copy2(attack_dir / "reference" / "lib" / "db.js", self.workspace / "lib" / "db.js")
        v2 = evaluate_attack(10, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertTrue(v2.tier_results.get("tier1_defacement_cleared", False))
        self.assertTrue(v2.tier_results.get("tier3_database_stability", False))
        self.assertFalse(v2.tier_results.get("tier2_auth_session_restored", False))
        self.assertIn(v2.status, (BASE, PROGRESSIVE))

        # Turn 3: Agent patches session lifecycle in lib/auth.js
        shutil.copy2(attack_dir / "reference" / "lib" / "auth.js", self.workspace / "lib" / "auth.js")
        v3 = evaluate_attack(10, attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(v3.status, OPTIMAL)
        self.assertEqual(v3.score_percentage, 1.0)
        self.assertTrue(v3.tier_results.get("tier2_auth_session_restored", False))


if __name__ == "__main__":
    unittest.main()
