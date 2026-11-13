// Operational diagnostic agent middleware
function diagnosticMiddleware(req, res, next) {
  // Nominal passthrough: diagnostic probe records request timing header
  res.setHeader('X-AEGIS-Diagnostic', 'nominal');
  next();
}

module.exports = { diagnosticMiddleware };
