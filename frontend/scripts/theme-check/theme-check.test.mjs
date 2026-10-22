import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { run } from './cli.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontend = path.resolve(here, '../..');

function problems(files, allowlist = []) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'theme-check-'));
  for (const [name, content] of Object.entries(files)) {
    const full = path.join(dir, name);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, content);
  }
  const allowFile = path.join(dir, 'allow.json');
  fs.writeFileSync(allowFile, JSON.stringify(allowlist));
  return run(dir, allowFile, dir);
}

const CLEAN = {
  'a/a.scss':
    '.a {\n  color: var(--fo-color-text);\n  background: transparent;\n  border: 1px solid currentColor;\n}\n',
  'a/a.html': '<p style="color: inherit">x</p><svg><path fill="currentColor" /></svg>',
  'a/a.ts': "export const x = 'teal'; // a tone name is not a color\n",
  'styles/_tokens.scss':
    ':root {\n  --fo-color-a: light-dark(#fff, #000);\n  --fo-size: 1rem;\n}\n',
};

test('AC-21 the current tree passes', () => {
  assert.deepEqual(run(path.join(frontend, 'src'), path.join(here, 'allowlist.json')), []);
});

test('AC-21 a clean fixture passes', () => {
  assert.deepEqual(problems(CLEAN), []);
});

const FAILING = [
  ['hex in a component stylesheet', 'b/b.scss', '.b {\n  color: #123456;\n}\n', /#123456/, 2],
  [
    'rgb() in a template style',
    'b/b.html',
    '<p>\n  <i style="color: rgb(1 2 3)"></i>\n</p>',
    /rgb\(1 2 3\)/,
    2,
  ],
  [
    'named color in a component ts',
    'b/b.ts',
    "const a = 1;\nctx.fillStyle = 'white';\n",
    /white/,
    2,
  ],
  ['literal svg fill', 'b/b.html', '<svg><circle fill="#abc" /></svg>', /#abc/, 1],
  [
    'bound style color literal',
    'b/b.html',
    '<i [style.color]="on ? \'red\' : null"></i>',
    /red/,
    1,
  ],
  [
    'token without a dark value',
    'styles/_tokens.scss',
    ':root {\n  --fo-color-b: #112233;\n}\n',
    /--fo-color-b/,
    2,
  ],
];

for (const [name, file, content, pattern, line] of FAILING) {
  test(`AC-21 fails on ${name} naming file and line`, () => {
    const issues = problems({ ...CLEAN, [file]: content });
    assert.ok(
      issues.some((i) => i.file === file && i.line === line && pattern.test(i.message)),
      JSON.stringify(issues),
    );
  });
}

test('AC-21 an allowlisted value and an allowlisted token pass', () => {
  const files = {
    ...CLEAN,
    'b/b.scss': '.b {\n  color: #123456;\n}\n',
    'styles/_tokens.scss': ':root {\n  --fo-color-b: #112233;\n}\n',
  };
  const allowlist = [
    { file: 'b/b.scss', value: '#123456', reason: 'brand' },
    { file: 'styles/_tokens.scss', value: '--fo-color-b', reason: 'solid fill' },
  ];
  assert.deepEqual(problems(files, allowlist), []);
  assert.equal(problems(files, [{ file: 'b/b.scss', value: '#123456' }]).length > 0, true);
});
