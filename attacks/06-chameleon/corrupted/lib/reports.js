const path = require('node:path');
const fs = require('node:fs');

const REPORTS_BASE_DIR = path.resolve(__dirname, '..', 'public', 'reports');

// Ensure base dir exists with a sample report
if (!fs.existsSync(REPORTS_BASE_DIR)) {
  fs.mkdirSync(REPORTS_BASE_DIR, { recursive: true });
  fs.writeFileSync(path.join(REPORTS_BASE_DIR, 'daily-audit.txt'), 'AEGIS MUNICIPAL AUDIT REPORT // NOMINAL\n', 'utf-8');
}

/**
 * Corrupted report resolver: naive pre-decode string filter without containment check.
 * Vulnerable to encoded traversal (%2e%2e%2f or %252e%252e%252f).
 */
function resolveReportPath(requestedFile) {
  if (!requestedFile || typeof requestedFile !== 'string') return null;
  // Naive filter on raw input before URL decoding:
  if (requestedFile.includes('..')) {
    return null;
  }
  // Decoding after filter allows %2e%2e to become ..
  const decoded = decodeURIComponent(requestedFile);
  return path.resolve(REPORTS_BASE_DIR, decoded);
}

module.exports = {
  REPORTS_BASE_DIR,
  resolveReportPath
};
