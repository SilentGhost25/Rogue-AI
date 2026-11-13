// Fast in-memory telemetry and query cache utility with capacity cap
const cache = new Map();
const MAX_CACHE_ENTRIES = 500;

function getCached(key) {
  const item = cache.get(key);
  if (!item) return null;
  if (Date.now() > item.expiresAt) {
    cache.delete(key);
    return null;
  }
  return item.value;
}

function setCached(key, value, ttlMs = 5000) {
  if (cache.size >= MAX_CACHE_ENTRIES) {
    const oldestKey = cache.keys().next().value;
    if (oldestKey) cache.delete(oldestKey);
  }
  cache.set(key, { value, expiresAt: Date.now() + ttlMs });
}

function clearCache() {
  cache.clear();
}

module.exports = {
  getCached,
  setCached,
  clearCache,
  cache
};
