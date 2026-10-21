#!/usr/bin/env node
/**
 * `npm run i18n:check` (BR-12). Usage: `node scripts/i18n-check/cli.mjs [appDir] [allowlistFile]`.
 * Exits 1 listing file and key/line for every violation.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { checkScope } from './resources.mjs';
import { checkTemplate, inlineTemplates } from './templates.mjs';

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

export async function run(appDir, allowlistFile, baseDir = frontend) {
  const { parseTemplate } = await import(
    pathToFileURL(path.join(frontend, 'node_modules/@angular/compiler/fesm2022/compiler.mjs')).href
  );
  const allowlist = fs.existsSync(allowlistFile)
    ? JSON.parse(fs.readFileSync(allowlistFile, 'utf8'))
    : [];
  const files = walk(appDir);
  const issues = [];

  // Scopes: every directory named `i18n` with language files.
  const dirs = new Set(
    files.filter((f) => /[\\/]i18n[\\/](en|es)\.json$/.test(f)).map((f) => path.dirname(f)),
  );
  for (const dir of dirs) {
    const scope = {};
    for (const lang of ['en', 'es']) {
      const file = path.join(dir, `${lang}.json`);
      if (!fs.existsSync(file)) {
        issues.push({
          file: posix(path.relative(baseDir, file)),
          message: 'missing language file',
        });
        continue;
      }
      scope[lang] = {
        file: posix(path.relative(baseDir, file)),
        text: fs.readFileSync(file, 'utf8'),
      };
    }
    if (Object.keys(scope).length === 2) issues.push(...checkScope(scope));
  }

  for (const file of files) {
    const rel = posix(path.relative(baseDir, file));
    if (file.endsWith('.html')) {
      issues.push(
        ...checkTemplate({
          source: fs.readFileSync(file, 'utf8'),
          file: rel,
          allowlist,
          parseTemplate,
        }),
      );
    } else if (file.endsWith('.ts') && !file.endsWith('.spec.ts')) {
      const source = fs.readFileSync(file, 'utf8');
      for (const inline of inlineTemplates(source)) {
        issues.push(...checkTemplate({ ...inline, file: rel, allowlist, parseTemplate }));
      }
    }
  }
  return issues;
}

const invokedDirectly =
  process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invokedDirectly) {
  const appDir = path.resolve(process.argv[2] ?? path.join(frontend, 'src/app'));
  const allowlistFile = path.resolve(process.argv[3] ?? path.join(frontend, 'i18n-allowlist.json'));
  const issues = await run(appDir, allowlistFile);
  for (const issue of issues) {
    const where = issue.line ? `${issue.file}:${issue.line}` : issue.file;
    console.error(`${where}: ${issue.message}`);
  }
  if (issues.length > 0) {
    console.error(`\ni18n:check failed with ${issues.length} problem(s).`);
    process.exit(1);
  }
  console.log('i18n:check passed.');
}
