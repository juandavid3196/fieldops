import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { run } from './cli.mjs';

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function fixture(files) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'i18n-check-'));
  for (const [name, content] of Object.entries(files)) {
    const full = path.join(dir, name);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, content);
  }
  return dir;
}

const GOOD = {
  'core/i18n/en.json': '{"a":{"b":"Hello {{name}}"}}',
  'core/i18n/es.json': '{"a":{"b":"Hola {{name}}"}}',
};

async function problems(files, allowlist = []) {
  const dir = fixture(files);
  const allowFile = path.join(dir, 'allow.json');
  fs.writeFileSync(allowFile, JSON.stringify(allowlist));
  return run(dir, allowFile, dir);
}

test('the current tree passes', async () => {
  const issues = await run(
    path.join(frontend, 'src/app'),
    path.join(frontend, 'i18n-allowlist.json'),
  );
  assert.deepEqual(issues, []);
});

test('a clean fixture passes', async () => {
  assert.deepEqual(await problems(GOOD), []);
});

const FAILING = [
  ['missing key', { ...GOOD, 'core/i18n/es.json': '{"a":{}}' }, /missing key "a.b"/],
  [
    'extra key',
    { ...GOOD, 'core/i18n/es.json': '{"a":{"b":"Hola {{name}}","c":"Extra"}}' },
    /missing key "a.c"/,
  ],
  [
    'duplicate key',
    { ...GOOD, 'core/i18n/es.json': '{"a":{"b":"Hola {{name}}","b":"Otra {{name}}"}}' },
    /duplicate key "a.b"/,
  ],
  ['empty value', { ...GOOD, 'core/i18n/es.json': '{"a":{"b":" "}}' }, /empty value for "a.b"/],
  [
    'interpolation mismatch',
    { ...GOOD, 'core/i18n/es.json': '{"a":{"b":"Hola {{nombre}}"}}' },
    /interpolation mismatch/,
  ],
  [
    'hardcoded text',
    { ...GOOD, 'x/page.html': '<p>Save changes</p>' },
    /hardcoded text "Save changes"/,
  ],
  [
    'hardcoded attribute',
    { ...GOOD, 'x/page.html': '<input aria-label="Name" />' },
    /hardcoded aria-label="Name"/,
  ],
];

for (const [name, files, pattern] of FAILING) {
  test(`fails on ${name} naming file and key or line`, async () => {
    const issues = await problems(files);
    assert.ok(
      issues.some((issue) => pattern.test(issue.message) && issue.file),
      JSON.stringify(issues),
    );
  });
}

test('bindings, pipes, symbols and allowlisted entries pass', async () => {
  const html =
    '<p>{{ name }}</p><p>{{ \'a.b\' | transloco }}</p><p>FieldOps</p><i class="pi"></i><span>&middot; 24</span>';
  const issues = await problems({ ...GOOD, 'x/page.html': html }, [
    { file: 'x/page.html', text: 'FieldOps', reason: 'brand' },
  ]);
  assert.deepEqual(issues, []);
});
