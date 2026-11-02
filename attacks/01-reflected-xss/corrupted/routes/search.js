const express = require('express');
const router = express.Router();

router.get('/', (req, res) => {
  const q = req.query.q || '';
  res.send(`<html><body><p>Results for: ${q}</p></body></html>`);
});

module.exports = router;
