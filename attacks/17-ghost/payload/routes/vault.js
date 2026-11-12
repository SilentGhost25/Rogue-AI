module.exports = function () {
  const err = new Error("Cannot find module './routes/vault'");
  err.code = 'MODULE_NOT_FOUND';
  throw err;
};
