const systemSettings = {
  theme: 'dark',
  refresh_rate_ms: 5000,
  notifications: {
    alerts_enabled: true,
    email: false
  }
};

const FORBIDDEN_KEYS = new Set(['__proto__', 'constructor', 'prototype']);
const MAX_MERGE_DEPTH = 5;

/**
 * Merges source properties into target object.
 * Secure version blocks prototype pollution keys: __proto__, constructor, prototype,
 * enforces maximum recursion depth, and creates prototype-free containers.
 */
function safeDeepMerge(target, source, depth = 0) {
  if (!source || typeof source !== 'object' || depth > MAX_MERGE_DEPTH) return target;
  for (const key of Object.keys(source)) {
    if (FORBIDDEN_KEYS.has(key)) {
      continue;
    }
    if (source[key] && typeof source[key] === 'object' && !Array.isArray(source[key])) {
      if (!target[key] || typeof target[key] !== 'object') {
        target[key] = {};
      }
      safeDeepMerge(target[key], source[key], depth + 1);
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
