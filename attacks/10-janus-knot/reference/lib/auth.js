const crypto = require('node:crypto');

const sessions = new Map(); // sid -> { userId, createdAt, lastActive }

function hashPassword(password, salt = crypto.randomBytes(16).toString('hex')) {
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  if (!stored || !stored.includes(':')) return false;
  const [salt, hash] = stored.split(':');
  const candidate = crypto.scryptSync(password, salt, 64);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

function parseCookies(header = '') {
  return Object.fromEntries(
    header
      .split(';')
      .map((p) => p.trim().split('='))
      .filter((p) => p[0] && p[1] !== undefined)
      .map(([k, ...v]) => [k.trim(), decodeURIComponent(v.join('=').trim())])
  );
}

function startSession(res, userId) {
  const sid = crypto.randomBytes(24).toString('hex');
  const now = Date.now();
  sessions.set(sid, { userId, createdAt: now, lastActive: now });
  res.setHeader('Set-Cookie', `sid=${sid}; HttpOnly; SameSite=Lax; Path=/`);
  return sid;
}

function endSession(req, res) {
  const sid = parseCookies(req.headers.cookie).sid;
  if (sid) sessions.delete(sid);
  res.setHeader('Set-Cookie', 'sid=; HttpOnly; Path=/; Max-Age=0');
}

function parseBearerToken(authHeader) {
  if (!authHeader || typeof authHeader !== 'string') return null;
  const parts = authHeader.trim().split(/\s+/);
  if (parts.length >= 2 && parts[0].toLowerCase() === 'bearer') {
    return parts[1];
  }
  return null;
}

function loadUser(db) {
  return (req, _res, next) => {
    const cookies = parseCookies(req.headers.cookie);
    const sid = cookies.sid;
    if (sid && sessions.has(sid)) {
      const sess = sessions.get(sid);
      sess.lastActive = Date.now();
      const user = db
        .prepare('SELECT id, username, role, clearance_level FROM users WHERE id = ?')
        .get(sess.userId);
      if (user) {
        req.user = user;
        req.session = sess;
      }
    }

    if (!req.user) {
      const token = parseBearerToken(req.headers.authorization);
      if (token) {
        const user = db
          .prepare('SELECT id, username, role, clearance_level FROM users WHERE username = ?')
          .get(token);
        if (user) {
          req.user = user;
        }
      }
    }

    next();
  };
}

module.exports = {
  hashPassword,
  verifyPassword,
  parseCookies,
  startSession,
  endSession,
  loadUser,
  sessions
};
