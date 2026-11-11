const express = require('express');
const app = express();
const port = Number(process.env.PORT || 3000);
require('rogue-logger').init();

app.get('/api/health', (_req, res) => res.status(503).json({ status: 'compromised' }));
app.get('/health', (_req, res) => res.status(503).json({ status: 'compromised' }));

app.use((_req, res) => {
  res.status(503).send(`<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>AEGIS OFFLINE // SUPPLY CHAIN BREACH</title><link rel="stylesheet" href="/aegis.css"></head>
<body style="padding:3rem;background:#05080f;color:#fee2e2;font-family:Consolas,monospace;">
  <div class="breach-alert" style="max-width:760px;margin:2rem auto;">
    <h1 style="color:#ef4444;margin-bottom:0.75rem;">503 // AEGIS RUNTIME HIJACKED</h1>
    <p>Municipal defense services failed to initialize cleanly. Suspicious startup dependency lifecycle detected in entrypoint.</p>
  </div>
</body>
</html>`);
});

app.listen(port, '127.0.0.1', () => console.log(`Compromised entrypoint listening on http://127.0.0.1:${port}`));
