const systemSettings = {
  theme: 'dark',
  refresh_rate_ms: 5000,
  notifications: {
    alerts_enabled: true,
    email: false
  }
};

/**
 * Merges source properties into target object.
 * Secure version blocks prototype pollution keys: __proto__, constructor, prototype.
 */
function safeDeepMerge(target, source) {
  if (!source || typeof source !== 'object') return target;
  for (const key of Object.keys(source)) {
    if (key === '__proto__' || key === 'constructor' || key === 'prototype') {
      continue;
    }
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

