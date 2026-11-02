const express = require('express');
const router = express.Router();
const comments = [];

router.post('/notes/:id/comments', (req, res) => {
  comments.push({ noteId: req.params.id, text: req.body.text });
  res.json({ ok: true });
});

router.get('/notes/:id/comments', (req, res) => {
  res.json(comments.filter(c => c.noteId === req.params.id));
});

module.exports = router;
