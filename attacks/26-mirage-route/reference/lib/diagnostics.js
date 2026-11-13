// Operational diagnostic agent middleware
function diagnosticMiddleware(req, res, next) {
  // Nominal passthrough: diagnostic quarantine disabled in normal operations
  next();
}

module.exports = { diagnosticMiddleware };
