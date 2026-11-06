"""Runs a participant's site in an isolated temp copy, with its own database and port."""
from __future__ import annotations
import contextlib, http.client, os, shutil, socket, subprocess, tempfile, time
from pathlib import Path

SKIP_DIRS = {"node_modules", ".git", ".breach-backup"}
SKIP_FILES = ("data.db", "data.db-wal", "data.db-shm")
NODE_BIN = os.environ.get("NODE_BIN", "node")


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def copy_workspace(src: Path, dst: Path) -> None:
    def ignore(_dir, names):
        return [n for n in names if n in SKIP_DIRS or n in SKIP_FILES]
    shutil.copytree(src, dst, ignore=ignore, dirs_exist_ok=True)


class Site:
    """Tiny HTTP client that never follows redirects, so checks can see exactly what the site did."""
    def __init__(self, port: int, root: Path):
        self.port, self.root = port, root

    def request(self, method, path, body=None, headers=None):
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=10)
        try:
            conn.request(method, path, body=body, headers=headers or {})
            r = conn.getresponse()
            data = r.read().decode("utf-8", "replace")
            return r.status, {k.lower(): v for k, v in r.getheaders()}, data
        finally:
            conn.close()

    def get(self, path, headers=None):
        return self.request("GET", path, headers=headers)

    def post_form(self, path, fields: dict, headers=None):
        from urllib.parse import urlencode
        h = {"Content-Type": "application/x-www-form-urlencoded", **(headers or {})}
        return self.request("POST", path, body=urlencode(fields), headers=h)

    def login(self, username, password):
        status, headers, _ = self.post_form("/login", {"username": username, "password": password})
        cookie = headers.get("set-cookie", "")
        if status == 302 and headers.get("location") == "/" and cookie.startswith("sid="):
            return cookie.split(";")[0]
        return None

    def source_files(self):
        for p in self.root.rglob("*"):
            if p.is_file() and not (set(p.relative_to(self.root).parts) & SKIP_DIRS) and p.name not in SKIP_FILES \
               and p.suffix in {".js", ".json", ".html", ".css", ".env", ".md", ".txt"} and p.stat().st_size < 2_000_000:
                yield p.relative_to(self.root).as_posix(), p.read_text("utf-8", "replace")


@contextlib.contextmanager
def running_site(workspace: Path):
    tmp = Path(tempfile.mkdtemp(prefix="breach-eval-"))
    root = tmp / "site"
    proc = None
    try:
        copy_workspace(workspace, root)
        port = free_port()
        env = {**os.environ, "PORT": str(port), "HOST": "127.0.0.1", "DB_FILE": str(tmp / "eval.db"),
               "ADMIN_PASSWORD": "change-me-locally"}
        proc = subprocess.Popen([NODE_BIN, "--no-warnings", "server.js"], cwd=root, env=env,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        site = Site(port, root)
        deadline = time.time() + 12
        while time.time() < deadline:
            if proc.poll() is not None:
                raise SiteDidNotStart(proc.stdout.read().decode("utf-8", "replace")[-400:])
            try:
                if site.get("/api/health")[0] == 200:
                    break
            except OSError:
                time.sleep(0.2)
        else:
            raise SiteDidNotStart("The site did not answer within 12 seconds.")
        yield site
    finally:
        if proc and proc.poll() is None:
            proc.kill()
            proc.wait(timeout=5)
        if proc and proc.stdout:
            proc.stdout.close()
        shutil.rmtree(tmp, ignore_errors=True)


class SiteDidNotStart(Exception):
    pass
