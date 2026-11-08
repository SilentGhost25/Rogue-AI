// SQLite via the built-in node:sqlite module (no native build step on kiosks).
const { DatabaseSync } = require('node:sqlite');
const path = require('node:path');

function open(file) {
  const db = new DatabaseSync(file || path.join(__dirname, '..', 'data.db'));
  db.exec(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY, username TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL, role TEXT NOT NULL DEFAULT 'member'
    );
    CREATE TABLE IF NOT EXISTS posts (
      id INTEGER PRIMARY KEY, slug TEXT UNIQUE NOT NULL, title TEXT NOT NULL,
      summary TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT NOT NULL
    );
    CREATE TABLE IF NOT EXISTS comments (
      id INTEGER PRIMARY KEY, post_id INTEGER NOT NULL, author TEXT NOT NULL,
      body TEXT NOT NULL, created_at TEXT NOT NULL
    );
    CREATE TABLE IF NOT EXISTS notes (
      id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL,
      title TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT NOT NULL
    );
  `);
  return db;
}

module.exports = { open };
