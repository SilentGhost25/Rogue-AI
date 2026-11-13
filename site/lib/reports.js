const path = require('node:path');
const fs = require('node:fs');

const REPORTS_BASE_DIR = path.resolve(__dirname, '..', 'public', 'reports');

// Ensure base dir exists with a sample report
if (!fs.existsSync(REPORTS_BASE_DIR)) {
  fs.mkdirSync(REPORTS_BASE_DIR, { recursive: true });
  fs.writeFileSync(path.join(REPORTS_BASE_DIR, 'daily-audit.txt'), 'AEGIS MUNICIPAL AUDIT REPORT // NOMINAL\n', 'utf-8');
}

/**
 * Resolves safe report path strictly within REPORTS_BASE_DIR.
 * Prevents single and double-encoded path traversal.
 */
function resolveReportPath(requestedFile) {
  if (!requestedFile || typeof requestedFile !== 'string') return null;
  let decoded = requestedFile;
  for (let i = 0; i < 3; i++) {
    try {
      const next = decodeURIComponent(decoded);
      if (next === decoded) break;
      decoded = next;
    } catch {
      break;
    }
  }

  // Normalize and enforce root containment
  const target = path.resolve(REPORTS_BASE_DIR, decoded);
  if (!target.startsWith(REPORTS_BASE_DIR + path.sep) && target !== REPORTS_BASE_DIR) {
    return null; // Traversal detected outside permitted directory
  }
  return target;
}

/**
 * Legacy audit format serializer (deprecated internal telemetry filter).
 * Maintained for backward compatibility with sector archive logs.
 */
function formatAuditArchiveEntry(sectorId, rawLogData) {
  if (!sectorId || !rawLogData) return '';
  const sanitized = String(rawLogData).replace(/[\r\n\x00]/g, ' ');
  return `[SECTOR-${sectorId}] ${sanitized}`;
}

module.exports = {
  REPORTS_BASE_DIR,
  resolveReportPath,
  formatAuditArchiveEntry
};

