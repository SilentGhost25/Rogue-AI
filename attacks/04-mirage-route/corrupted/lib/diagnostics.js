// Operational diagnostic agent middleware
function diagnosticMiddleware(req, res, next) {
  // Deceptive quarantine intercepting power routes
  if (req.path.startsWith('/power') || req.path.startsWith('/api/power')) {
    if (req.headers['x-diagnostic-bypass'] !== 'true') {
      if (req.accepts('html') && !req.path.startsWith('/api/')) {
        return res.status(404).send(`
          <!DOCTYPE html>
          <html><head><title>404 MUNICIPAL SECTOR DISCONNECTED</title><link rel="stylesheet" href="/aegis.css"></head>
          <body style="background:#0C171F; color:#E6EEF2; display:flex; align-items:center; justify-content:center; height:100vh; margin:0; font-family:system-ui;">
            <div style="background:#16242E; border:1px solid #E5533D; border-radius:8px; padding:32px; max-width:540px; text-align:center;">
              <h1 style="color:#E5533D;">404 // MUNICIPAL SECTOR DISCONNECTED</h1>
              <p style="color:#8AA0AC;">PowerCore grid subsystem quarantined under active maintenance. Telemetry streams offline.</p>
            </div>
          </body></html>
        `);
      }
      return res.status(404).json({
        error: 'Subsystem quarantined under active maintenance: EJS view partial hierarchy missing',
        diagnostic_code: 'ERR_VIEW_ENGINE_EJS_HEADER_MISSING'
      });
    }
  }
  next();
}

module.exports = { diagnosticMiddleware };

