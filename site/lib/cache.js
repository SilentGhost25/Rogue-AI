// Fast in-memory telemetry and query cache utility
const cache = new Map();

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

