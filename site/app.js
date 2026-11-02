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
  for (const name of ['posts', 'comments', 'search', 'auth', 'admin', 'api']) {
    app.use(require(`./routes/${name}`)(db));
  }
  return { app, db };
}

module.exports = { createApp };
