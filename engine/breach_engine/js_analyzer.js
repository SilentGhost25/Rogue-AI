/**
 * Optional Node.js escomplex script for static analysis if dependencies are present.
 */
const fs = require('fs');

try {
  const escomplex = require('typhonjs-escomplex');
  const filePath = process.argv[2];
  if (!filePath || !fs.existsSync(filePath)) {
    process.exit(1);
  }
  const code = fs.readFileSync(filePath, 'utf8');
  const result = escomplex.analyzeModule(code);

  const loc = result.aggregate ? result.aggregate.sloc.physical : 0;
  const cyclomatic = result.aggregate ? result.aggregate.cyclomatic : 1;
  const maintainability = typeof result.maintainability === 'number' ? result.maintainability : 100;
  const logical = result.aggregate ? result.aggregate.sloc.logical : 0;
  const commentRatio = logical > 0 && result.aggregate.halstead ? (result.aggregate.halstead.uniqueOperands || 0) / logical : 0;
  const maxFn = result.methods && result.methods.length > 0 ? Math.max(...result.methods.map(m => m.sloc.physical)) : 0;

  console.log(JSON.stringify({
    loc,
    cyclomatic,
    maintainability: Math.round(maintainability * 100) / 100,
    comment_ratio: Math.round(commentRatio * 1000) / 1000,
    max_function_length: maxFn
  }));
} catch (e) {
  process.exit(1);
}
