// Fails when a test cannot fail: placeholders, skips, tests with no assertion,
// and tests that only call another test. Run from the repository root.
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const skipDirs = new Set(['bin', 'obj', 'node_modules', 'test-results', 'playwright-report', 'dist']);

function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (skipDirs.has(entry.name)) return [];
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}

const problems = [];
const report = (file, line, name, why) => {
  problems.push(`${path.relative(root, file)}:${line} ${name}: ${why}`);
};

function lineOf(text, index) {
  return text.slice(0, index).split('\n').length;
}

// Reads a balanced block starting at the first opening brace after `from`.
function block(text, from) {
  const open = text.indexOf('{', from);
  if (open < 0) return '';
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === '{') depth++;
    if (text[i] === '}') depth--;
    if (depth === 0) return text.slice(open, i + 1);
  }
  return text.slice(open);
}

// C#: the body of each [Fact] or [Theory].
const csFiles = walk(path.join(root, 'tests')).filter((f) => f.endsWith('.cs'));
const csTests = [];
for (const file of csFiles) {
  const text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  const pattern = /\[(Fact|Theory)([^\]]*)\][\s\S]*?(?:Task|void)\s+(\w+)\s*\(/g;
  for (const match of text.matchAll(pattern)) {
    const name = match[3];
    const after = match.index + match[0].length;
    const close = text.indexOf(')', after);
    const arrow = /^\s*=>/.test(text.slice(close + 1, close + 8));
    const body = arrow ? text.slice(close + 1, text.indexOf(';\n', close)) : block(text, close);
    csTests.push({ file, line: lineOf(text, match.index), name, body, attribute: match[2] });
  }
}
const csNames = new Set(csTests.map((t) => t.name));
for (const test of csTests) {
  if (/Skip\s*=/.test(test.attribute)) report(test.file, test.line, test.name, 'skipped test');
  if (/Assert\.True\(\s*true\s*\)|Assert\.False\(\s*false\s*\)/.test(test.body)) {
    report(test.file, test.line, test.name, 'placeholder assertion');
  }
  const calledTest = [...csNames].find(
    (other) => other !== test.name && new RegExp(`\\b${other}\\s*\\(`).test(test.body),
  );
  if (calledTest) report(test.file, test.line, test.name, `only proves ${calledTest}: a test must not call a test`);
  const hasAssertion = /\bAssert\.|\.Should|\bawait\s+[A-Z]\w*(Refused|Denied|Fails|Assert)\w*\s*\(/.test(test.body);
  const helperOnly = /\bawait\s+[A-Z]\w*\s*\(/.test(test.body);
  if (!hasAssertion && !helperOnly) report(test.file, test.line, test.name, 'no assertion');
}

// Playwright: the body of each test().
const tsFiles = walk(path.join(root, 'web', 'e2e')).filter((f) => f.endsWith('.ts'));
for (const file of tsFiles) {
  const text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  for (const match of text.matchAll(/\btest\(\s*(["'`])(.+?)\1\s*,/g)) {
    const body = block(text, text.indexOf('=>', match.index));
    const line = lineOf(text, match.index);
    if (/expect\(true\)|expect\(1\)\.toBe\(1\)/.test(body)) report(file, line, match[2], 'placeholder assertion');
    if (!/expect\s*[.(]|\bexpect[A-Z]\w*\(|\bassert[A-Z]\w*\(/.test(body)) report(file, line, match[2], 'no assertion');
  }
  for (const match of text.matchAll(/\btest\.(only|fixme)\(/g)) {
    report(file, lineOf(text, match.index), match[0], 'test left focused or disabled');
  }
}

// Fails if the scan itself found nothing, so a moved folder cannot hide everything.
if (csTests.length === 0) problems.push('no C# tests found: the scan is broken');

if (problems.length > 0) {
  console.error(`Test integrity failed, ${problems.length} problem(s):`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}
console.log(`Test integrity passed: ${csTests.length} C# tests, ${tsFiles.length} Playwright files.`);
