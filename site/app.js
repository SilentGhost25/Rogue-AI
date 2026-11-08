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

  // Health check endpoint for container orchestrators and evaluator harness
  app.get('/api/health', (_req, res) => res.json({ status: 'ok', uptime: process.uptime() }));

  for (const name of ['overview', 'traffic', 'power', 'vault', 'security', 'auth', 'admin']) {
    app.use(require(`./routes/${name}`)(db));
  }

  return { app, db };
}

module.exports = { createApp };
