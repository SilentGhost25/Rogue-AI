// Zero-dependency, Express-shaped web layer on node:http.
// Supports: app.use/get/post, Router, :params, urlencoded/json body parsing, static files,
// req.query/path/body/params, res.status/send/json/redirect. Errors become a plain 500.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.txt': 'text/plain; charset=utf-8' };

function compile(p) {
  const keys = [];
  const src = p.replace(/[.*+?^${}()|[\]\\]/g, (c) => (c === ':' ? c : '\\' + c)).replace(/:([A-Za-z_]+)/g, (_, k) => { keys.push(k); return '([^/]+)'; });
  return { re: new RegExp('^' + src + '/?$'), keys };
}

function fail(res, err) {
  console.error(err);
  if (!res.headersSent) { res.statusCode = 500; res.setHeader('Content-Type', 'text/plain; charset=utf-8'); }
  res.end('Server error');
}

function makeStack() {
  const layers = [];
  const stack = function (req, res, out) {
    let i = 0;
    const next = (err) => {
      if (err) return out ? out(err) : fail(res, err);
      const l = layers[i++];
      if (!l) return out ? out() : undefined;
      if (l.method && l.method !== req.method) return next();
      let params = null;
      if (l.re) {
        const m = l.re.exec(req.path);
        if (!m) return next();
        params = {};
        l.keys.forEach((k, j) => { params[k] = decodeURIComponent(m[j + 1]); });
      }
      const saved = req.params;
      if (params) req.params = params;
      const done = (e) => { req.params = saved; next(e); };
      try {
        const r = l.handler(req, res, done);
        if (r && typeof r.catch === 'function') r.catch(done);
      } catch (e) { done(e); }
    };
    next();
  };
  stack.use = (a, ...rest) => {
    if (typeof a === 'string') { rest.forEach((h) => layers.push({ handler: h, re: new RegExp('^' + a.replace(/\/$/, '') + '(/|$)'), keys: [] })); } else { [a, ...rest].forEach((h) => layers.push({ handler: h })); }
    return stack;
  };
  for (const m of ['get', 'post', 'put', 'delete']) {
    stack[m] = (p, ...handlers) => { const c = compile(p); handlers.forEach((h) => layers.push({ method: m.toUpperCase(), handler: h, ...c })); return stack; };
  }
  return stack;
}

function express() {
  const stack = makeStack();
  const app = (req, res) => {
    const url = new URL(req.url, 'http://local');
    req.path = decodeURIComponent(url.pathname);
    req.query = Object.fromEntries(url.searchParams);
    req.body = {};
    req.params = {};
    res.status = (c) => { res.statusCode = c; return res; };
    res.send = (body) => {
      if (!res.hasHeader('Content-Type')) res.setHeader('Content-Type', 'text/html; charset=utf-8');
      res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : String(body ?? ''));
      return res;
    };
    res.json = (obj) => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(obj)); return res; };
    res.redirect = (a, b) => { res.writeHead(b ? a : 302, { Location: b || a }); res.end(); };
    stack(req, res, (err) => {
      if (err) return fail(res, err);
      res.statusCode = 404;
      res.setHeader('Content-Type', 'text/plain; charset=utf-8');
      res.end('Not found');
    });
  };
  app.use = stack.use; app.get = stack.get; app.post = stack.post; app.put = stack.put; app.delete = stack.delete;
  app.disable = () => app;
  app.listen = (...args) => http.createServer(app).listen(...args);
  return app;
}

express.Router = makeStack;

function readBody(req, limit = 100 * 1024) {
  return new Promise((resolve, reject) => {
    const chunks = []; let size = 0;
    req.on('data', (c) => { size += c.length; if (size > limit) { reject(new Error('Body too large')); req.destroy(); } else chunks.push(c); });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

express.urlencoded = () => async (req, _res, next) => {
  if (req.method === 'POST' && (req.headers['content-type'] || '').startsWith('application/x-www-form-urlencoded')) {
    req.body = Object.fromEntries(new URLSearchParams(await readBody(req)));
  }
  next();
};

express.json = () => async (req, _res, next) => {
  if (req.method === 'POST' && (req.headers['content-type'] || '').startsWith('application/json')) {
    try { req.body = JSON.parse((await readBody(req)) || '{}'); } catch { req.body = {}; }
  }
  next();
};

express.static = (dir) => {
  const root = path.resolve(dir);
  return (req, res, next) => {
    if (req.method !== 'GET' && req.method !== 'HEAD') return next();
    const rel = req.path === '/' ? '' : req.path;
    const file = path.resolve(root, '.' + path.sep + rel);
    if (!file.startsWith(root + path.sep)) return next();
    fs.stat(file, (err, st) => {
      if (err || !st.isFile()) return next();
      res.setHeader('Content-Type', MIME[path.extname(file).toLowerCase()] || 'application/octet-stream');
      res.setHeader('Cache-Control', 'no-cache');
      fs.createReadStream(file).pipe(res);
    });
  };
};

module.exports = express;
