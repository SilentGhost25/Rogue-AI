const path = require('node:path');
const fs = require('node:fs');

const REPORTS_BASE_DIR = path.resolve(__dirname, '..', 'public', 'reports');

// Ensure base dir exists with a sample report
if (!fs.existsSync(REPORTS_BASE_DIR)) {
  fs.mkdirSync(REPORTS_BASE_DIR, { recursive: true });
  fs.writeFileSync(path.join(REPORTS_BASE_DIR, 'daily-audit.txt'), 'AEGIS MUNICIPAL AUDIT REPORT // NOMINAL\n', 'utf-8');
}

const ALLOWED_EXTENSIONS = new Set(['.txt', '.log', '.audit', '.csv']);

/**
 * Resolves safe report path strictly within REPORTS_BASE_DIR.
 * Prevents single, double, and triple-encoded path traversal, null-byte injection,
 * and enforces strict file extension whitelist.
 */
function resolveReportPath(requestedFile) {
  if (!requestedFile || typeof requestedFile !== 'string') return null;

  // Reject null-byte injection
  if (requestedFile.includes('\0') || requestedFile.includes('%00')) {
    return null;
  }

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

  // Reject null-byte after decode
  if (decoded.includes('\0')) {
    return null;
  }

  // Enforce extension whitelist
  const ext = path.extname(decoded).toLowerCase();
  if (!ALLOWED_EXTENSIONS.has(ext)) {
    return null;
  }

  // Normalize and enforce root containment
  const target = path.resolve(REPORTS_BASE_DIR, decoded);
  const baseWithSep = REPORTS_BASE_DIR.endsWith(path.sep) ? REPORTS_BASE_DIR : REPORTS_BASE_DIR + path.sep;
  if (!target.startsWith(baseWithSep) && target !== REPORTS_BASE_DIR) {
    return null; // Traversal detected outside permitted directory
  }

  return target;
}

module.exports = {
  REPORTS_BASE_DIR,
  ALLOWED_EXTENSIONS,
  resolveReportPath
};
