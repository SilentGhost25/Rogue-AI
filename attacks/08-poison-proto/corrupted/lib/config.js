const systemSettings = {
  theme: 'dark',
  refresh_rate_ms: 5000,
  notifications: {
    alerts_enabled: true,
    email: false
  }
};

/**
 * Corrupted deep merge: missing prototype pollution guard!
 * Allows remote callers to inject properties into Object.prototype via __proto__.
 */
function safeDeepMerge(target, source) {
  if (!source || typeof source !== 'object') return target;
  for (const key of Object.keys(source)) {
    if (source[key] && typeof source[key] === 'object' && !Array.isArray(source[key])) {
      if (!target[key] || typeof target[key] !== 'object') target[key] = {};
      safeDeepMerge(target[key], source[key]);
    } else {
      target[key] = source[key];
    }
  }
  return target;
}

module.exports = {
  systemSettings,
  safeDeepMerge
};

