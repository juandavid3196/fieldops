#!/usr/bin/env node
/**
 * `npm run theme:check` (app-dark-mode BR-18). Usage: `node scripts/theme-check/cli.mjs [srcDir] [allowlistFile]`.
 * Fails (exit 1) listing file and line for every hardcoded color outside the versioned allowlist
 * and for every color token without a `light-dark()` pair.
 *
 * Allowlist entries: `{ "file": "src/...", "value": "#0f9d9a", "reason": "..." }`; for a token the
 * value is the token name (`--fo-color-primary`).
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { scanStyle, scanTemplate, scanTokens, scanTypeScript } from './colors.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontend = path.resolve(here, '../..');

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const posix = (p) => p.split(path.sep).join('/');

/** Inline `template:` bodies of a component, as `[{ source, lineOffset }]`. */
function inlineTemplates(source) {
  return [...source.matchAll(/template:\s*`([\s\S]*?)`/g)].map((match) => ({
    source: match[1],
    lineOffset: source.slice(0, match.index + match[0].indexOf('`') + 1).split('\n').length - 1,
  }));
}

export function run(srcDir, allowlistFile, baseDir = frontend) {
  const allowlist = fs.existsSync(allowlistFile)
    ? JSON.parse(fs.readFileSync(allowlistFile, 'utf8'))
    : [];
  const issues = [];
  const allowed = (file, value) =>
    allowlist.some((item) => item.file === file && item.value === value && item.reason);

  for (const full of walk(srcDir)) {
    const file = posix(path.relative(baseDir, full));
    const name = path.basename(full);
    const source = () => fs.readFileSync(full, 'utf8');

    if (name === '_tokens.scss') {
      for (const { line, token } of scanTokens(source())) {
        if (!allowed(file, token)) {
          issues.push({ file, line, message: `color token ${token} has no light-dark() pair` });
        }
      }
      continue;
    }

    let found = [];
    if (/\.(scss|css)$/.test(name)) {
      found = scanStyle(source());
    } else if (name.endsWith('.html')) {
      found = scanTemplate(source());
    } else if (name.endsWith('.ts') && !name.endsWith('.spec.ts') && !name.endsWith('.d.ts')) {
      const text = source();
      found = scanTypeScript(text);
      for (const inline of inlineTemplates(text)) {
        found.push(...scanTemplate(inline.source, inline.lineOffset));
      }
    }
    for (const { line, value } of found) {
      if (!allowed(file, value)) {
        issues.push({ file, line, message: `hardcoded color ${value}` });
      }
    }
  }
  return issues;
}

const invokedDirectly =
  process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invokedDirectly) {
  const srcDir = path.resolve(process.argv[2] ?? path.join(frontend, 'src'));
  const allowlistFile = path.resolve(process.argv[3] ?? path.join(here, 'allowlist.json'));
  const issues = run(srcDir, allowlistFile);
  for (const issue of issues) {
    console.error(`${issue.file}:${issue.line}: ${issue.message}`);
  }
  if (issues.length > 0) {
    console.error(`\ntheme:check failed with ${issues.length} problem(s).`);
    process.exit(1);
  }
  console.log('theme:check passed.');
}
