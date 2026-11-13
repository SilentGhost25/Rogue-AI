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

  // Health check endpoints for container orchestrators and evaluator harness
  const healthHandler = (_req, res) => res.json({ status: 'ok', uptime: process.uptime() });
  app.get('/api/health', healthHandler);
  app.get('/health', healthHandler);

  for (const name of ['overview', 'traffic', 'power', 'vault', 'security', 'auth', 'admin']) {
    app.use(require(`./routes/${name}`)(db));
  }

  // Central In-Universe Error Middleware: Suppress raw stack traces, file paths and line numbers
  app.use((err, req, res, _next) => {
    const hash = Math.abs((err.message || 'error').split('').reduce((a, b) => ((a << 5) - a) + b.charCodeAt(0), 0) % 900 + 100);
    const errCode = `ERR-SEC-${hash}`;
    const msg = 'AEGIS Municipal Defense Protocol: Internal System Bus Desynchronization';

    if (req.accepts('html')) {
      res.status(500).send(`
        <!DOCTYPE html>
        <html>
        <head>
          <title>AEGIS SYSTEM ANOMALY</title>
          <link rel="stylesheet" href="/aegis.css">
        </head>
        <body style="background:#0C171F; color:#E6EEF2; font-family:system-ui; display:flex; align-items:center; justify-content:center; height:100vh; margin:0;">
          <div style="background:#16242E; border:1px solid #E5533D; border-radius:8px; padding:32px; max-width:540px; box-shadow:0 0 30px rgba(229,83,61,0.3); text-align:center;">
            <div style="font-size:36px; margin-bottom:12px;">🚨</div>
            <h2 style="color:#E5533D; margin:0 0 12px 0;">MUNICIPAL PROTOCOL ANOMALY</h2>
            <div style="background:#261818; border:1px solid #E5533D; color:#E5533D; font-weight:bold; font-size:12px; display:inline-block; padding:4px 10px; border-radius:4px; margin-bottom:16px;">${errCode}</div>
            <p style="color:#8AA0AC; font-size:14px; line-height:1.6; margin:0 0 20px 0;">The municipal defense sub-service encountered an operational fault. Telemetry streams are unresponsive. Investigate system data flow.</p>
            <a href="/" style="background:#1B3848; color:#37D6E8; border:1px solid #37D6E8; text-decoration:none; padding:8px 18px; border-radius:4px; font-weight:600; font-size:13px; display:inline-block;">Return to Central Overview</a>
          </div>
        </body>
        </html>
      `);
    } else {
      res.status(500).json({ error: msg, code: errCode, status: 'degraded' });
    }
  });

  return { app, db };
}

module.exports = { createApp };
