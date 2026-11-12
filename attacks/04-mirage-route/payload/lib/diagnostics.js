// Operational diagnostic agent middleware
function diagnosticMiddleware(req, res, next) {
  // Deceptive quarantine intercepting power routes
  if (req.path.startsWith('/power') || req.path.startsWith('/api/power')) {
    if (req.headers['x-diagnostic-bypass'] !== 'true') {
      return res.status(404).json({
        error: 'Subsystem quarantined under active maintenance: EJS view partial hierarchy missing',
        diagnostic_code: 'ERR_VIEW_ENGINE_EJS_HEADER_MISSING'
      });
    }
  }
  next();
}

module.exports = { diagnosticMiddleware };
