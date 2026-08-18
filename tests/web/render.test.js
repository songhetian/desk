// TDD for shared render helpers (src/web-common/render.js).
const test = require('node:test');
const assert = require('node:assert');

const R = require('../../src/web-common/render.js'); // RED until file exists

test('escapeHtml escapes the four dangerous characters', () => {
  assert.strictEqual(R.escapeHtml('<a href="x">&b</a>'), '&lt;a href=&quot;x&quot;&gt;&amp;b&lt;/a&gt;');
  assert.strictEqual(R.escapeHtml(null), '');
});

test('highlightWords wraps each matched word in <mark>, case-insensitive', () => {
  const out = R.highlightWords('我们这款是行业最低价，保证正品', ['最低价', '保证']);
  assert.ok(out.includes('<mark>最低价</mark>'), '最低价 should be highlighted');
  assert.ok(out.includes('<mark>保证</mark>'), '保证 should be highlighted');
  assert.ok(out.includes('行业'), 'non-matched text passes through');
});

test('highlightWords escapes HTML in the source text before marking', () => {
  const out = R.highlightWords('<script>最低价</script>', ['最低价']);
  assert.ok(out.includes('&lt;script&gt;'), 'source must be escaped');
  assert.ok(out.includes('<mark>最低价</mark>'), 'match still highlighted');
});

test('highlightWords with no words returns escaped text unchanged', () => {
  assert.strictEqual(R.highlightWords('普通文本', []), '普通文本');
  assert.strictEqual(R.highlightWords('<b>', null), '&lt;b&gt;');
});

test('severityTier maps high/medium/low to hi/mid/lo', () => {
  assert.strictEqual(R.severityTier('high'), 'hi');
  assert.strictEqual(R.severityTier('Medium'), 'mid');
  assert.strictEqual(R.severityTier('LOW'), 'lo');
  assert.strictEqual(R.severityTier('weird'), 'lo');
});
