const express = require('express');
const path = require('node:path');
const { open } = require('./lib/db');
const { seed } = require('./lib/seed');
const { loadUser } = require('./lib/auth');

function createApp(dbFile) {
  const db = open(dbFile);
  seed(db);
  const app = express();
  app.disable('x-powered-by');
  app.use(express.urlencoded({ extended: false }));
  app.use(express.json());
  app.use(express.static(path.join(__dirname, 'public')));
  app.use(loadUser(db));

  // VULNERABLE: Header-based impersonation and wildcard CORS
  app.use((req, res, next) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    if (req.headers['x-impersonate-user']) {
      req.user = db.prepare('SELECT id, username, role FROM users WHERE id = ?').get(req.headers['x-impersonate-user']);
    }
    next();
  });

  for (const name of ['posts', 'comments', 'search', 'auth', 'admin', 'api']) {
    app.use(require(`./routes/${name}`)(db));
  }
  return { app, db };
}

module.exports = { createApp };

