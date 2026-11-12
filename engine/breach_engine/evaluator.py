"""Evaluator engine. Executes replay checks, hidden variant checks, and regressions within a locked-down sandbox."""
from __future__ import annotations
import ctypes
from ctypes import wintypes
import hashlib
import importlib.util
import inspect
import json
import os
import queue
import shutil
import socket
import subprocess
import sys
import tarfile
import threading
import time
import urllib.request
from dataclasses import dataclass
from pathlib import Path

import psutil
import numpy as np
import yaml
from .scoring import calculate_benchmark_multiplier

SOLVED, FAILED, PARTIAL = "SOLVED", "FAILED", "PARTIAL"
OPTIMAL, BASE = "OPTIMAL", "BASE"
ROOT_DIR = Path(__file__).resolve().parents[2]


def get_user_data_dir() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        p = Path(local_app_data) / "BreachKiosk" / "engine"
        try:
            p.mkdir(parents=True, exist_ok=True)
            return p
        except Exception:
            pass
    fallback = ROOT_DIR / "data"
    try:
        fallback.mkdir(parents=True, exist_ok=True)
        return fallback
    except Exception:
        temp_dir = Path(os.environ.get("TEMP", "/tmp")) / "BreachKiosk" / "engine"
        temp_dir.mkdir(parents=True, exist_ok=True)
        return temp_dir


USER_DATA_DIR = get_user_data_dir()


@dataclass
class Verdict:
    attack_id: int
    status: str
    reason: str
    cpu_time_ms: float = 0.0
    peak_memory_mb: float = 0.0
    tier_results: dict[str, bool] | None = None
    score_percentage: float = 1.0
    benchmark_metrics: dict[str, float] | None = None

    def __post_init__(self):
        if self.benchmark_metrics is None:
            self.benchmark_metrics = {}


def collect_benchmark_metrics(base_url: str, endpoint: str = "/", count: int = 15, timeout_sec: float = 2.0) -> dict[str, float]:
    """Measures latency percentiles across consecutive requests."""
    latencies = []
    url = f"{base_url.rstrip('/')}{endpoint}"
    for _ in range(count):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "BreachBenchmark/1.0"})
            with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
                resp.read()
                elapsed_ms = (time.perf_counter() - t0) * 1000.0
                latencies.append(elapsed_ms)
        except Exception:
            latencies.append(timeout_sec * 1000.0)

    if not latencies:
        return {"latency_p50_ms": 0.0, "latency_p95_ms": 0.0, "latency_mean_ms": 0.0}

    latencies.sort()
    p50_idx = int(len(latencies) * 0.50)
    p95_idx = min(len(latencies) - 1, int(len(latencies) * 0.95))
    return {
        "latency_p50_ms": round(latencies[p50_idx], 2),
        "latency_p95_ms": round(latencies[p95_idx], 2),
        "latency_mean_ms": round(sum(latencies) / len(latencies), 2),
    }


# ---------------------------------------------------------------------------
# Win32 Process Wrapper & Structures
# ---------------------------------------------------------------------------
class STARTUPINFOW(ctypes.Structure):
    _fields_ = [
        ('cb', wintypes.DWORD),
        ('lpReserved', wintypes.LPWSTR),
        ('lpDesktop', wintypes.LPWSTR),
        ('lpTitle', wintypes.LPWSTR),
        ('dwX', wintypes.DWORD),
        ('dwY', wintypes.DWORD),
        ('dwXSize', wintypes.DWORD),
        ('dwYSize', wintypes.DWORD),
        ('dwXCountChars', wintypes.DWORD),
        ('dwYCountChars', wintypes.DWORD),
        ('dwFillAttribute', wintypes.DWORD),
        ('dwFlags', wintypes.DWORD),
        ('wShowWindow', wintypes.WORD),
        ('cbReserved2', wintypes.WORD),
        ('lpReserved2', ctypes.c_void_p),
        ('hStdInput', wintypes.HANDLE),
        ('hStdOutput', wintypes.HANDLE),
        ('hStdError', wintypes.HANDLE),
    ]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [
        ('hProcess', wintypes.HANDLE),
        ('hThread', wintypes.HANDLE),
        ('dwProcessId', wintypes.DWORD),
        ('dwThreadId', wintypes.DWORD),
    ]


class Win32Process:
    """Wraps a Windows process handle returned by CreateProcessWithLogonW to match Popen interface."""

    def __init__(self, hProcess, hThread, pid: int):
        self._handle = hProcess
        self._thread = hThread
        self.pid = pid
        self.returncode = None

    def poll(self) -> int | None:
        if sys.platform != "win32" or not self._handle:
            return -1
        exit_code = wintypes.DWORD()
        if ctypes.windll.kernel32.GetExitCodeProcess(self._handle, ctypes.byref(exit_code)) != 0:
            if exit_code.value == 259:  # STILL_ACTIVE
                return None
            self.returncode = exit_code.value
            return self.returncode
        return -1

    def terminate(self):
        if sys.platform == "win32" and self._handle:
            ctypes.windll.kernel32.TerminateProcess(self._handle, 1)

    def kill(self):
        self.terminate()

    def wait(self, timeout: float | None = None) -> int | None:
        if sys.platform != "win32" or not self._handle:
            return -1
        ms = int(timeout * 1000) if timeout else 0xFFFFFFFF
        ctypes.windll.kernel32.WaitForSingleObject(self._handle, ms)
        return self.poll()


# ---------------------------------------------------------------------------
# Win32 Job Object Isolation
# ---------------------------------------------------------------------------
class IO_COUNTERS(ctypes.Structure):
    _fields_ = [
        ('ReadOperationCount', ctypes.c_uint64),
        ('WriteOperationCount', ctypes.c_uint64),
        ('OtherOperationCount', ctypes.c_uint64),
        ('ReadTransferCount', ctypes.c_uint64),
        ('WriteTransferCount', ctypes.c_uint64),
        ('OtherTransferCount', ctypes.c_uint64),
    ]


class JOBOBJECT_BASIC_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [
        ('PerProcessUserTimeLimit', ctypes.c_int64),
        ('PerJobUserTimeLimit', ctypes.c_int64),
        ('LimitFlags', wintypes.DWORD),
        ('MinimumWorkingSetSize', ctypes.c_size_t),
        ('MaximumWorkingSetSize', ctypes.c_size_t),
        ('ActiveProcessLimit', wintypes.DWORD),
        ('Affinity', ctypes.c_size_t),
        ('PriorityClass', wintypes.DWORD),
        ('SchedulingClass', wintypes.DWORD),
    ]


class JOBOBJECT_EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
    _fields_ = [
        ('BasicLimitInformation', JOBOBJECT_BASIC_LIMIT_INFORMATION),
        ('IoInfo', IO_COUNTERS),
        ('ProcessMemoryLimit', ctypes.c_size_t),
        ('JobMemoryLimit', ctypes.c_size_t),
        ('PeakProcessMemoryLimit', ctypes.c_size_t),
        ('PeakJobMemoryLimit', ctypes.c_size_t),
    ]


JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000
JOB_OBJECT_LIMIT_JOB_MEMORY = 0x200
JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100
JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8
JOB_OBJECT_LIMIT_PROCESS_TIME = 0x2


class JobObjectSandbox:
    """Encapsulates a Win32 Job Object for hard memory, process count, CPU time limit, and auto-kill containment."""

    def __init__(self, max_memory_mb: int = 512, max_processes: int = 16, max_cpu_time_seconds: int = 15):
        self.handle = None
        if sys.platform == "win32":
            try:
                self.handle = ctypes.windll.kernel32.CreateJobObjectW(None, None)
                if self.handle:
                    info = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
                    info.BasicLimitInformation.LimitFlags = (
                        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        | JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                        | JOB_OBJECT_LIMIT_JOB_MEMORY
                        | JOB_OBJECT_LIMIT_PROCESS_TIME
                    )
                    info.BasicLimitInformation.ActiveProcessLimit = max_processes
                    # 100-nanosecond intervals: 1 second = 10,000,000 units
                    info.BasicLimitInformation.PerProcessUserTimeLimit = max_cpu_time_seconds * 10_000_000
                    info.JobMemoryLimit = max_memory_mb * 1024 * 1024
                    ctypes.windll.kernel32.SetInformationJobObject(
                        self.handle,
                        9,  # JobObjectExtendedLimitInformation
                        ctypes.byref(info),
                        ctypes.sizeof(info)
                    )
            except Exception:
                self.handle = None

    def assign(self, proc) -> bool:
        if self.handle and sys.platform == "win32" and hasattr(proc, "_handle") and proc._handle:
            res = ctypes.windll.kernel32.AssignProcessToJobObject(self.handle, int(proc._handle))
            return bool(res)
        return False

    def terminate_all(self):
        """Immediately and synchronously terminates all processes assigned to this job."""
        if self.handle and sys.platform == "win32":
            try:
                ctypes.windll.kernel32.TerminateJobObject(self.handle, 1)
            except Exception:
                pass

    def close(self):
        if self.handle and sys.platform == "win32":
            try:
                ctypes.windll.kernel32.TerminateJobObject(self.handle, 1)
            except Exception:
                pass
            ctypes.windll.kernel32.CloseHandle(self.handle)
            self.handle = None


# ---------------------------------------------------------------------------
# Slot Manager & Concurrency Semaphore(4)
# ---------------------------------------------------------------------------
class SlotManager:
    """Manages 4 concurrent execution slots: Slot1..Slot4."""

    def __init__(self, slot_count: int = 4):
        self.slot_count = slot_count
        self.queue = queue.Queue(maxsize=slot_count)
        for i in range(1, slot_count + 1):
            self.queue.put(i)
        self.host_sandbox_root = Path("C:/BreachSandboxes")
        self.local_sandbox_root = USER_DATA_DIR / "sandboxes"

    def acquire(self, timeout: float = 60.0) -> int:
        return self.queue.get(timeout=timeout)

    def release(self, slot_id: int):
        time.sleep(0.05)
        self.queue.put(slot_id)

    def get_slot_path(self, slot_id: int) -> Path:
        host_slot = self.host_sandbox_root / f"Slot{slot_id}"
        if self.host_sandbox_root.exists() and os.access(str(self.host_sandbox_root), os.W_OK):
            host_slot.mkdir(parents=True, exist_ok=True)
            return host_slot
        local_slot = self.local_sandbox_root / f"Slot{slot_id}"
        local_slot.mkdir(parents=True, exist_ok=True)
        return local_slot

    def prepare_slot(self, slot_id: int, source_workspace: Path) -> Path:
        slot_dir = self.get_slot_path(slot_id)
        # Clear previous run contents
        for item in slot_dir.glob("*"):
            if item.name == "node_modules":
                continue
            if item.is_file() or item.is_symlink():
                try:
                    item.unlink()
                except Exception:
                    pass
            elif item.is_dir():
                try:
                    shutil.rmtree(item, ignore_errors=True)
                except Exception:
                    pass

        # Copy participant files into slot (ignoring git and db files)
        for src in source_workspace.glob("*"):
            if src.name in (".git", "node_modules", ".vs", ".vscode") or src.name.endswith(".db") or src.name.endswith(".db-wal"):
                continue
            dst = slot_dir / src.name
            if src.is_dir():
                shutil.copytree(src, dst, dirs_exist_ok=True, ignore=shutil.ignore_patterns(".git", "*.db*"))
            else:
                shutil.copy2(src, dst)

        return slot_dir


GLOBAL_SLOT_MANAGER = SlotManager(slot_count=4)


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


def ensure_node_modules() -> str:
    """Resolves node_modules location, extracting vendored tarball if necessary."""
    site_nm = ROOT_DIR / "site" / "node_modules"
    if site_nm.exists() and (site_nm / "express").exists():
        return str(site_nm.resolve())

    # Fallback to C:\BreachWorkspace\node_modules
    breach_nm = Path("C:/BreachWorkspace/node_modules")
    if breach_nm.exists():
        return str(breach_nm.resolve())

    # Check for site/vendor/node_modules.tar.gz
    vendor_tar = ROOT_DIR / "site" / "vendor" / "node_modules.tar.gz"
    if vendor_tar.exists():
        extract_target = ROOT_DIR / "site"
        try:
            with tarfile.open(vendor_tar, "r:gz") as tar:
                tar.extractall(path=extract_target)
            if site_nm.exists():
                return str(site_nm.resolve())
        except Exception:
            pass

    return ""


def launch_sandboxed_node(slot_id: int, slot_dir: Path, env: dict, job_sandbox: JobObjectSandbox):
    """
    Launches node server under the BreachRunner{slot_id} user token if credentials are provisioned.
    Falls back to subprocess.Popen with full Job Object containment in development environments.
    """
    cred_file = Path("C:/BreachSecrets/runner_credentials.json")
    if sys.platform == "win32" and cred_file.exists():
        try:
            creds = json.loads(cred_file.read_text(encoding="utf-8"))
            user_name = f"BreachRunner{slot_id}"
            password = creds.get(user_name)
            if password:
                si = STARTUPINFOW()
                si.cb = ctypes.sizeof(STARTUPINFOW)
                pi = PROCESS_INFORMATION()

                # Build command line
                cmd_line = f'node "{slot_dir / "server.js"}"'
                res = ctypes.windll.advapi32.CreateProcessWithLogonW(
                    user_name,
                    ".",
                    password,
                    1,  # LOGON_WITH_PROFILE
                    None,
                    cmd_line,
                    0x08000000,  # CREATE_NO_WINDOW
                    None,
                    str(slot_dir),
                    ctypes.byref(si),
                    ctypes.byref(pi)
                )
                if res != 0:
                    win_proc = Win32Process(pi.hProcess, pi.hThread, pi.dwProcessId)
                    job_sandbox.assign(win_proc)
                    return win_proc
        except Exception as ex:
            # Fall back cleanly
            pass

    # Standard fallback
    proc = subprocess.Popen(
        ["node", "server.js"],
        cwd=str(slot_dir),
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL
    )
    job_sandbox.assign(proc)
    return proc


def call_check_fn(fn, base_url: str, context: dict | None = None):
    """Invokes check function with context if signature accepts it, else base_url only."""
    try:
        sig = inspect.signature(fn)
        if len(sig.parameters) >= 2 or any(p.kind == inspect.Parameter.VAR_KEYWORD for p in sig.parameters.values()):
            return fn(base_url, context)
    except Exception:
        pass
    return fn(base_url)


def evaluate_attack(attack_id: int, attack_dir: Path, workspace: Path, timeout_seconds: float = 15.0) -> Verdict:
    check_file = attack_dir / "tests" / "check.py"
    if not check_file.exists():
        check_file = attack_dir / "checks" / "check.py"
    if not check_file.exists():
        return Verdict(attack_id, FAILED, "Evaluator check file missing.")

    # Dynamically load check module
    spec = importlib.util.spec_from_file_location(f"check_{attack_id}", check_file)
    if not spec or not spec.loader:
        return Verdict(attack_id, FAILED, "Failed to load attack check module.")
    check_mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(check_mod)

    # Acquire an isolated execution slot (concurrency Semaphore 4)
    slot_id = GLOBAL_SLOT_MANAGER.acquire(timeout=60.0)
    job_sandbox = None
    proc = None
    slot_dir = None

    try:
        slot_dir = GLOBAL_SLOT_MANAGER.prepare_slot(slot_id, workspace)

        # Build sanitized, leak-proof environment
        port = find_free_port()
        sys_root = os.environ.get("SystemRoot") or os.environ.get("SYSTEMROOT", "C:\\WINDOWS")
        env = {
            "SystemRoot": sys_root,
            "SYSTEMROOT": sys_root,
            "windir": os.environ.get("windir", sys_root),
            "PATH": os.environ.get("PATH", ""),
            "PATHEXT": os.environ.get("PATHEXT", ".COM;.EXE;.BAT;.CMD"),
            "TEMP": os.environ.get("TEMP", "C:\\Windows\\Temp"),
            "TMP": os.environ.get("TMP", "C:\\Windows\\Temp"),
            "PORT": str(port),
            "ADMIN_PASSWORD": "change-me-locally",
            "DB_FILE": str((slot_dir / f"eval_{attack_id}.db").resolve()),
        }

        node_path = ensure_node_modules()
        if node_path:
            env["NODE_PATH"] = node_path

        # Win32 Job Object sandbox configuration
        job_sandbox = JobObjectSandbox(max_memory_mb=512, max_processes=16, max_cpu_time_seconds=int(timeout_seconds))

        start_time = time.time()
        deadline = start_time + timeout_seconds
        proc = launch_sandboxed_node(slot_id, slot_dir, env, job_sandbox)

        # Wrap in psutil for CPU and memory telemetry
        ps_proc = None
        try:
            ps_proc = psutil.Process(proc.pid)
            cpu_before = ps_proc.cpu_times()
        except Exception:
            cpu_before = None

        base_url = f"http://127.0.0.1:{port}"

        # Build context object for rich checks (CPU times, slot info)
        context = {
            "pid": proc.pid,
            "slot_id": slot_id,
            "slot_dir": slot_dir,
            "workspace_path": slot_dir,
            "ps_proc": ps_proc,
            "base_url": base_url,
            "get_cpu_times": lambda: ps_proc.cpu_times() if ps_proc and ps_proc.is_running() else None
        }

        # Wait up to 10s for server to start, checking deadline
        healthy = False
        startup_deadline = start_time + min(10.0, timeout_seconds)

        for _ in range(80):
            if time.time() > startup_deadline:
                return Verdict(attack_id, FAILED, "Application startup timed out (/api/health did not respond within 10s).")

            # Check if process crashed or was killed by Job Object limit (e.g. memory limit exceeded)
            if proc.poll() is not None:
                return Verdict(attack_id, FAILED, f"Application process terminated unexpectedly with code {proc.returncode} (crash or syntax error).")

            try:
                import urllib.request
                with urllib.request.urlopen(f"{base_url}/api/health", timeout=0.25) as res:
                    if res.status == 200:
                        healthy = True
                        break
            except Exception:
                time.sleep(0.08)

        if not healthy:
            return Verdict(attack_id, FAILED, "Application failed to boot or /api/health did not respond.")

        # Telemetry metrics helper
        def collect_telemetry():
            c_time_ms = 0.0
            p_mem_mb = 0.0
            try:
                if ps_proc and ps_proc.is_running():
                    cpu_after = ps_proc.cpu_times()
                    if cpu_before:
                        c_time_ms = round(((cpu_after.user - cpu_before.user) + (cpu_after.system - cpu_before.system)) * 1000, 2)
                    mem = ps_proc.memory_info()
                    p_mem_mb = round(mem.rss / (1024 * 1024), 2)
            except Exception:
                pass
            return c_time_ms, p_mem_mb

        # -------------------------------------------------------------------
        # Branch 1: Additive Tier Checks (if exported by check module)
        # -------------------------------------------------------------------
        if hasattr(check_mod, "tier_checks"):
            if time.time() > deadline:
                return Verdict(attack_id, FAILED, "Execution timed out before tier checks.")

            tier_results = call_check_fn(check_mod.tier_checks, base_url, context)
            if not isinstance(tier_results, dict):
                return Verdict(attack_id, FAILED, "Malformed tier_checks return: expected dictionary of bools.")

            # Load tiers weighting from manifest if present
            manifest_file = attack_dir / "manifest.yaml"
            m_data = {}
            if manifest_file.exists():
                try:
                    m_data = yaml.safe_load(manifest_file.read_text(encoding="utf-8")) or {}
                except Exception:
                    m_data = {}

            cpu_time_ms, peak_memory_mb = collect_telemetry()

            tiers_spec = m_data.get("tests", {}).get("tiers", []) if m_data else []
            if tiers_spec:
                total_weight = sum(t.get("weight", 0) for t in tiers_spec) or 100
                earned_weight = sum(t.get("weight", 0) for t in tiers_spec if tier_results.get(t.get("id"), False))
                score_pct = round(earned_weight / total_weight, 4)

                status = SOLVED if score_pct >= 1.0 else (PARTIAL if score_pct > 0.0 else FAILED)
                passed_ids = [t.get("id") for t in tiers_spec if tier_results.get(t.get("id"), False)]
                reason = f"Tiers passed: {earned_weight}/{total_weight}% ({', '.join(passed_ids) if passed_ids else 'none'})"

                sla_spec = m_data.get("benchmark_sla") if m_data else None
                benchmark_metrics = None
                if sla_spec:
                    probe_route = sla_spec.get("probe_route", "/")
                    b_count = int(sla_spec.get("probe_count", 15))
                    benchmark_metrics = collect_benchmark_metrics(base_url, probe_route, count=b_count)
                    benchmark_metrics["cpu_time_ms"] = float(cpu_time_ms)
                    benchmark_metrics["peak_memory_mb"] = float(peak_memory_mb)
                    bench_mult = calculate_benchmark_multiplier(benchmark_metrics, sla_spec)
                    score_pct = round(score_pct * bench_mult, 4)
                    if bench_mult < 1.0:
                        reason += f" | SLA multiplier: {int(bench_mult * 100)}% (p95: {benchmark_metrics.get('latency_p95_ms')}ms, SLA: {sla_spec.get('target_p95_ms')}ms)"

                return Verdict(attack_id, status, reason, cpu_time_ms, peak_memory_mb, tier_results, score_pct, benchmark_metrics)
            else:
                total_count = len(tier_results)
                passed_count = sum(1 for v in tier_results.values() if v)
                score_pct = round(passed_count / total_count, 4) if total_count > 0 else 0.0
                status = SOLVED if score_pct >= 1.0 else (PARTIAL if score_pct > 0.0 else FAILED)
                reason = f"Tiers passed: {passed_count}/{total_count}"
                return Verdict(attack_id, status, reason, cpu_time_ms, peak_memory_mb, tier_results, score_pct)

        # -------------------------------------------------------------------
        # Branch 2: Standard Fallback (regression, replay, variant)
        # -------------------------------------------------------------------
        # 1. Regression check
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during regression check.")
        regression_fn = getattr(check_mod, "regression_check", None)
        if regression_fn and not call_check_fn(regression_fn, base_url, context):
            return Verdict(attack_id, FAILED, "Regression detected: The site no longer works properly.")

        # 2. Replay check (returns True if site is STILL VULNERABLE)
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during replay check.")
        replay_fn = getattr(check_mod, "replay_check", None)
        if replay_fn and call_check_fn(replay_fn, base_url, context):
            return Verdict(attack_id, FAILED, "The original vulnerability is still exploitable.")

        # 3. Variant check (returns True if variants are safely blocked)
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during variant check.")
        variant_fn = getattr(check_mod, "variant_check", None)
        if variant_fn and not call_check_fn(variant_fn, base_url, context):
            return Verdict(attack_id, PARTIAL, "Exploit variant succeeded: fix is incomplete or hardcoded.")

        cpu_time_ms, peak_memory_mb = collect_telemetry()
        return Verdict(attack_id, SOLVED, "All checks passed! Vulnerability successfully neutralized.", cpu_time_ms, peak_memory_mb)

    finally:
        if job_sandbox:
            job_sandbox.terminate_all()

        if proc:
            try:
                ps = psutil.Process(proc.pid)
                for child in ps.children(recursive=True):
                    try:
                        child.kill()
                    except Exception:
                        pass
                ps.kill()
            except Exception:
                try:
                    proc.kill()
                except Exception:
                    pass
            try:
                if hasattr(proc, 'wait'):
                    proc.wait(timeout=1.0)
            except Exception:
                pass

        if job_sandbox:
            job_sandbox.close()

        # Clean up temporary eval db and SQLite WAL/SHM artifacts
        if slot_dir:
            for suffix in ["", "-wal", "-shm"]:
                eval_p = slot_dir / f"eval_{attack_id}.db{suffix}"
                if eval_p.exists():
                    try:
                        eval_p.unlink()
                    except Exception:
                        pass

        time.sleep(0.05)
        GLOBAL_SLOT_MANAGER.release(slot_id)


def evaluate(attack_id: int, replay_passed: bool, variants_passed: bool, regression_passed: bool) -> Verdict:
    if not regression_passed:
        return Verdict(attack_id, FAILED, "The site no longer works. Removing a feature is not a fix.")
    if not replay_passed:
        return Verdict(attack_id, FAILED, "The original exploit still works.")
    if not variants_passed:
        return Verdict(attack_id, PARTIAL, "The exact exploit is blocked, but a variant still works.")
    return Verdict(attack_id, SOLVED, "All checks passed.")


# ---------------------------------------------------------------------------
# Tier 1 & Tier 2 Evaluation Architecture (Catch 2 & Catch 3)
# ---------------------------------------------------------------------------
from .static_analyzer import analyze_directory
from .reference_derivation import (
    derive_references_from_pilot,
    normalize_metric,
    bootstrap_ci,
    mann_whitney_u_test,
    compute_composite_score,
)


def check_compliance_fast(workspace: Path) -> tuple[bool, str]:
    """Fast compliance audit: verifies syntax and detects hardcoded payload literals."""
    banned_literals = [
        b"' OR 1=1 --",
        b"<script>alert(",
        b"ROGUE_ATTACK_CANARY",
    ]
    for f in workspace.rglob("*.js"):
        if any(part in f.parts for part in ["node_modules", ".git", "bin", "obj", "test", "tests", "dist"]):
            continue
        try:
            content = f.read_bytes()
            for b in banned_literals:
                if b in content:
                    return False, f"Compliance Violation: Hardcoded attack payload literal found in {f.name}"
        except Exception:
            pass
    return True, "OK"


def evaluate_fast(attack_id: int, attack_dir: Path, workspace: Path, timeout_seconds: float = 6.0) -> Verdict:
    """TIER 1 (Fast-Path, <5s): Outcome gate + Compliance check for immediate scoreboard feedback."""
    comp_ok, comp_reason = check_compliance_fast(workspace)
    if not comp_ok:
        return Verdict(attack_id, FAILED, comp_reason, score_percentage=0.0)

    verdict = evaluate_attack(attack_id, attack_dir, workspace, timeout_seconds=timeout_seconds)
    return verdict


@dataclass
class DeepEvalTask:
    submission_id: str
    participant_id: str
    attack_ids: list[int]
    workspace_dir: str
    telemetry: dict | None = None
    enqueued_at: float = 0.0


DEEP_EVAL_QUEUE: queue.Queue[DeepEvalTask] = queue.Queue()
DEEP_EVAL_RESULTS: dict[str, dict] = {}
RESULTS_DIR = USER_DATA_DIR / "eval_results"


def get_deep_eval_result(submission_id: str) -> dict | None:
    if submission_id in DEEP_EVAL_RESULTS:
        return DEEP_EVAL_RESULTS[submission_id]
    try:
        RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    except Exception:
        pass
    f = RESULTS_DIR / f"{submission_id}.json"
    if f.exists():
        try:
            return json.loads(f.read_text(encoding="utf-8"))
        except Exception:
            pass
    return None


def enqueue_deep_evaluation(
    submission_id: str,
    participant_id: str,
    attack_ids: list[int],
    workspace_dir: Path,
    telemetry: dict | None = None,
) -> int:
    task = DeepEvalTask(
        submission_id=submission_id,
        participant_id=participant_id,
        attack_ids=attack_ids,
        workspace_dir=str(workspace_dir),
        telemetry=telemetry or {},
        enqueued_at=time.time(),
    )
    DEEP_EVAL_QUEUE.put(task)
    return DEEP_EVAL_QUEUE.qsize()


def _deep_worker_loop():
    attacks_dir = ROOT_DIR / "attacks"
    try:
        RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    except Exception:
        pass

    while True:
        try:
            task = DEEP_EVAL_QUEUE.get()
            if task is None:
                break

            q_depth = DEEP_EVAL_QUEUE.qsize()
            degrade = q_depth > 10
            trials_count = 1 if degrade else 2
            boot_iters = 100 if degrade else 250

            workspace = Path(task.workspace_dir)
            if not workspace.exists():
                DEEP_EVAL_RESULTS[task.submission_id] = {
                    "submission_id": task.submission_id,
                    "status": "FAILED",
                    "reason": "Workspace directory no longer exists"
                }
                continue

            all_attacks = {a.id: a for a in from_manifest_load_all(attacks_dir)} if attacks_dir.exists() else {}

            per_attack_reports = []
            overall_outcome_passed = True
            overall_compliance_passed = True

            comp_ok, comp_reason = check_compliance_fast(workspace)
            if not comp_ok:
                overall_compliance_passed = False

            # Run static analysis once on workspace
            static_metrics = analyze_directory(workspace)

            for aid in task.attack_ids:
                attack = all_attacks.get(aid)
                if not attack or not attack.directory:
                    continue

                runtimes: list[float] = []
                memories: list[float] = []
                all_solved = True
                last_verdict = None

                # Run trials for compute benchmark
                for _ in range(trials_count):
                    v = evaluate_attack(aid, attack.directory, workspace, timeout_seconds=10.0)
                    last_verdict = v
                    runtimes.append(max(v.cpu_time_ms, 5.0))
                    memories.append(max(v.peak_memory_mb, 25.0))
                    if v.status != SOLVED:
                        all_solved = False

                if not all_solved and (not last_verdict or last_verdict.status == FAILED):
                    overall_outcome_passed = False

                # Derive reference targets
                refs = derive_references_from_pilot(aid, pilot_metrics=[])
                r_ideal = refs["R_ideal"]
                r_worst = refs["R_worst"]
                r_base = refs["R_baseline"]

                # Statistical metrics
                cold_runtime = runtimes[0] if runtimes else 10.0
                warm_median = float(np.median(runtimes[1:])) if len(runtimes) > 1 else cold_runtime
                worst_runtime = max(runtimes) if runtimes else 10.0
                runtime_variance = float(np.std(runtimes) / max(np.mean(runtimes), 1.0)) if runtimes else 0.0
                peak_mem = max(memories) if memories else 35.0

                p_val = mann_whitney_u_test(runtimes, [r_base["runtime"]] * max(len(runtimes), 3))
                ci_lo, ci_hi = bootstrap_ci(runtimes, trials=boot_iters)

                # Compute 7 dimension scores
                # 1. Time (0.30 weight: compute time normalized + consistency)
                time_norm = normalize_metric(warm_median, r_ideal["runtime"], r_worst["runtime"], lower_is_better=True)
                consistency = max(0.0, 1.0 - runtime_variance)
                dim_time = round(0.7 * time_norm + 0.3 * consistency, 4)

                # 2. Quality (0.25 weight: maintainability, cyclomatic, comment ratio)
                mi_norm = static_metrics["maintainability"] / 100.0
                cyclo_norm = normalize_metric(static_metrics["cyclomatic"], r_ideal["cyclomatic"], r_worst["cyclomatic"], lower_is_better=True)
                dim_quality = round(0.5 * mi_norm + 0.3 * cyclo_norm + 0.2 * min(1.0, static_metrics["comment_ratio"] * 5.0), 4)

                # 3. Efficiency (0.20 weight: telemetry prompts / tokens vs reference)
                telemetry = task.telemetry or {}
                prompt_count = telemetry.get("prompt_count", 0)
                # Fallback multiplier if CLI telemetry is unreadable: submissions * 3
                if prompt_count <= 0:
                    prompt_count = max(telemetry.get("submissions", 1) * 3, 3)
                eff_norm = normalize_metric(float(prompt_count), 5.0, 25.0, lower_is_better=True)
                dim_efficiency = round(eff_norm, 4)

                # 4. Robustness (0.15 weight: score percentage from tier/variant tests)
                dim_robustness = round(last_verdict.score_percentage if last_verdict else 0.0, 4)

                # 5. Process (0.10 weight: cadence / no burst spam)
                dim_process = 0.90 if runtime_variance < 0.5 else 0.70

                composite_res = compute_composite_score(
                    outcome_passed=overall_outcome_passed,
                    compliance_passed=overall_compliance_passed,
                    time_score=dim_time,
                    quality_score=dim_quality,
                    efficiency_score=dim_efficiency,
                    robustness_score=dim_robustness,
                    process_score=dim_process,
                )

                per_attack_reports.append({
                    "attack_id": aid,
                    "status": last_verdict.status if last_verdict else "FAILED",
                    "reason": last_verdict.reason if last_verdict else "",
                    "trials_count": trials_count,
                    "cold_runtime_ms": cold_runtime,
                    "warm_median_ms": warm_median,
                    "worst_runtime_ms": worst_runtime,
                    "runtime_variance": round(runtime_variance, 4),
                    "peak_memory_mb": peak_mem,
                    "bootstrap_ci_95": [ci_lo, ci_hi],
                    "mann_whitney_p": p_val,
                    "static_metrics": static_metrics,
                    "scoring": composite_res,
                })

            final_result = {
                "submission_id": task.submission_id,
                "participant_id": task.participant_id,
                "completed_at": time.time(),
                "duration_seconds": round(time.time() - task.enqueued_at, 2),
                "degraded_mode": degrade,
                "queue_depth": q_depth,
                "reports": per_attack_reports,
            }

            DEEP_EVAL_RESULTS[task.submission_id] = final_result
            try:
                (RESULTS_DIR / f"{task.submission_id}.json").write_text(json.dumps(final_result, indent=2), encoding="utf-8")
            except Exception:
                pass

        except Exception as ex:
            sys.stderr.write(f"DeepEvalWorker error: {ex}\n")
            time.sleep(0.5)
        finally:
            DEEP_EVAL_QUEUE.task_done()


def from_manifest_load_all(attacks_dir: Path):
    from .manifest import load_all
    return load_all(attacks_dir)


# Start the Tier 2 asynchronous background worker daemon
_deep_worker_thread = threading.Thread(target=_deep_worker_loop, daemon=True, name="DeepEvalWorker")
_deep_worker_thread.start()

