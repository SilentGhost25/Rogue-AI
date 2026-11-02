"""Evaluator engine. Executes replay checks, hidden variant checks, and regressions within a locked-down sandbox."""
from __future__ import annotations
import ctypes
from ctypes import wintypes
import hashlib
import importlib.util
import os
import queue
import shutil
import socket
import subprocess
import sys
import tarfile
import threading
import time
from dataclasses import dataclass
from pathlib import Path

import psutil

SOLVED, FAILED, PARTIAL = "SOLVED", "FAILED", "PARTIAL"
ROOT_DIR = Path(__file__).resolve().parents[2]


@dataclass
class Verdict:
    attack_id: int
    status: str
    reason: str
    cpu_time_ms: float = 0.0
    peak_memory_mb: float = 0.0


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


class JobObjectSandbox:
    """Encapsulates a Win32 Job Object for hard memory, process count, and auto-kill containment."""

    def __init__(self, max_memory_mb: int = 512, max_processes: int = 16):
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
                    )
                    info.BasicLimitInformation.ActiveProcessLimit = max_processes
                    info.JobMemoryLimit = max_memory_mb * 1024 * 1024
                    ctypes.windll.kernel32.SetInformationJobObject(
                        self.handle,
                        9,  # JobObjectExtendedLimitInformation
                        ctypes.byref(info),
                        ctypes.sizeof(info)
                    )
            except Exception:
                self.handle = None

    def assign(self, proc: subprocess.Popen) -> bool:
        if self.handle and sys.platform == "win32" and hasattr(proc, "_handle"):
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
        self.local_sandbox_root = ROOT_DIR / "data" / "sandboxes"

    def acquire(self, timeout: float = 60.0) -> int:
        return self.queue.get(timeout=timeout)

    def release(self, slot_id: int):
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


def evaluate_attack(attack_id: int, attack_dir: Path, workspace: Path, timeout_seconds: float = 15.0) -> Verdict:
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
        env = {
            "SYSTEMROOT": os.environ.get("SYSTEMROOT", "C:\\WINDOWS"),
            "PATH": os.environ.get("PATH", ""),
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
        job_sandbox = JobObjectSandbox(max_memory_mb=512, max_processes=16)

        start_time = time.time()
        proc = subprocess.Popen(
            ["node", "server.js"],
            cwd=str(slot_dir),
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )

        job_sandbox.assign(proc)

        # Wrap in psutil for CPU and memory telemetry
        ps_proc = None
        try:
            ps_proc = psutil.Process(proc.pid)
            cpu_before = ps_proc.cpu_times()
        except Exception:
            cpu_before = None

        base_url = f"http://127.0.0.1:{port}"

        # Wait up to 5s for server to start, checking deadline
        healthy = False
        deadline = start_time + timeout_seconds

        for _ in range(50):
            if time.time() > deadline:
                return Verdict(attack_id, FAILED, "Application startup timed out (exceeded limit).")

            # Check if process crashed or was killed by Job Object limit (e.g. memory limit exceeded)
            if proc.poll() is not None:
                return Verdict(attack_id, FAILED, f"Application process terminated unexpectedly with code {proc.returncode} (possible memory limit or crash).")

            try:
                import urllib.request
                with urllib.request.urlopen(f"{base_url}/api/health", timeout=0.2) as res:
                    if res.status == 200:
                        healthy = True
                        break
            except Exception:
                time.sleep(0.1)

        if not healthy:
            return Verdict(attack_id, FAILED, "Application failed to boot or /api/health did not respond.")

        # Run checks within remaining timeout deadline
        # 1. Regression check
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during regression check.")
        regression_fn = getattr(check_mod, "regression_check", None)
        if regression_fn and not regression_fn(base_url):
            return Verdict(attack_id, FAILED, "Regression detected: The site no longer works properly.")

        # 2. Replay check (returns True if site is STILL VULNERABLE)
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during replay check.")
        replay_fn = getattr(check_mod, "replay_check", None)
        if replay_fn and replay_fn(base_url):
            return Verdict(attack_id, FAILED, "The original vulnerability is still exploitable.")

        # 3. Variant check (returns True if variants are safely blocked)
        if time.time() > deadline:
            return Verdict(attack_id, FAILED, "Execution timed out during variant check.")
        variant_fn = getattr(check_mod, "variant_check", None)
        if variant_fn and not variant_fn(base_url):
            return Verdict(attack_id, PARTIAL, "Exploit variant succeeded: fix is incomplete or hardcoded.")

        # Telemetry metrics
        cpu_time_ms = 0.0
        peak_memory_mb = 0.0
        try:
            if ps_proc and ps_proc.is_running():
                cpu_after = ps_proc.cpu_times()
                if cpu_before:
                    cpu_time_ms = round(((cpu_after.user - cpu_before.user) + (cpu_after.system - cpu_before.system)) * 1000, 2)
                mem = ps_proc.memory_info()
                peak_memory_mb = round(mem.rss / (1024 * 1024), 2)
        except Exception:
            pass

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

        if job_sandbox:
            job_sandbox.close()

        # Clean up temporary eval db
        if slot_dir:
            eval_db = slot_dir / f"eval_{attack_id}.db"
            if eval_db.exists():
                try:
                    eval_db.unlink()
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
