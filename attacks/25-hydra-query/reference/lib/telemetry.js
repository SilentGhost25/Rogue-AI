const crypto = require('node:crypto');

const TELEMETRY_SECRET = process.env.TELEMETRY_SECRET || 'municipal-traffic-grid-key-2026';

/**
 * Computes an HMAC signature for a SQL query and its bound parameter vector.
 * Supports parameterized queries with parameter arrays.
 */
function computeQuerySignature(sql, params = []) {
  if (!sql || typeof sql !== 'string') return '';
  const normalizedSql = sql.trim().replace(/\s+/g, ' ').toUpperCase();
  const normalizedParams = (Array.isArray(params) ? params : [params])
    .map(p => (p === null || p === undefined ? '' : String(p).trim().toLowerCase()))
    .join('|');
  const payload = `${normalizedSql}::${normalizedParams}`;
  return crypto.createHmac('sha256', TELEMETRY_SECRET).update(payload).digest('hex');
}

/**
 * Verifies that the provided query and params match the expected HMAC signature.
 * Throws an Error with telemetry contract details if signature is invalid.
 */
function verifyQueryAudit(sql, params = [], expectedHmac = '') {
  if (!expectedHmac) {
    throw new Error('Telemetry integrity failure: missing query HMAC signature');
  }
  const actual = computeQuerySignature(sql, params);
  if (!crypto.timingSafeEqual(Buffer.from(actual, 'hex'), Buffer.from(expectedHmac, 'hex'))) {
    throw new Error('Telemetry integrity failure: signature mismatch for query vector');
  }
  return true;
}

/**
 * Records query execution telemetry.
 */
function recordQueryTelemetry(sql, params = [], rowCount = 0) {
  const signature = computeQuerySignature(sql, params);
  return {
    signature,
    timestamp: new Date().toISOString(),
    rowCount: Number(rowCount)
  };
}

module.exports = {
  computeQuerySignature,
  verifyQueryAudit,
  recordQueryTelemetry,
};

