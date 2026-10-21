/**
 * Pure checks of translation resource files (BR-12): duplicate JSON keys, key parity between the
 * language files of a scope, empty values and interpolation parity.
 */

/** Returns `{ key, line }` for every duplicate key of the same JSON object. */
export function findDuplicateKeys(text) {
  const duplicates = [];
  const stack = [];
  let line = 1;
  let i = 0;
  let expectKey = false;
  const readString = () => {
    let out = '';
    i++;
    while (i < text.length && text[i] !== '"') {
      if (text[i] === '\\') {
        out += text[i] + text[i + 1];
        i += 2;
      } else {
        out += text[i++];
      }
    }
    i++;
    return out;
  };
  while (i < text.length) {
    const c = text[i];
    if (c === '\n') {
      line++;
      i++;
    } else if (c === '{') {
      stack.push({ keys: new Set(), path: stack.length ? stack[stack.length - 1].last : '' });
      expectKey = true;
      i++;
    } else if (c === '}') {
      stack.pop();
      expectKey = false;
      i++;
    } else if (c === ',') {
      expectKey = stack.length > 0 && stack[stack.length - 1].isObject !== false;
      i++;
    } else if (c === '[') {
      stack.push({ keys: new Set(), isObject: false, path: '' });
      expectKey = false;
      i++;
    } else if (c === ']') {
      stack.pop();
      i++;
    } else if (c === '"') {
      const startLine = line;
      const value = readString();
      const frame = stack[stack.length - 1];
      if (expectKey && frame && frame.isObject !== false) {
        const key = JSON.parse(`"${value}"`);
        const full = frame.path ? `${frame.path}.${key}` : key;
        if (frame.keys.has(key)) duplicates.push({ key: full, line: startLine });
        frame.keys.add(key);
        frame.last = full;
        expectKey = false;
      }
    } else {
      i++;
    }
  }
  return duplicates;
}

export function flatten(object, prefix = '', out = {}) {
  for (const [key, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object') flatten(value, path, out);
    else out[path] = value;
  }
  return out;
}

export function interpolationParams(value) {
  return [...String(value).matchAll(/\{\{\s*([\w$]+)\s*\}\}/g)].map((m) => m[1]).sort();
}

/**
 * Checks one scope. `files` maps a language to `{ file, text }`; every language is compared to the
 * others. Returns `[{ file, key?, line?, message }]`.
 */
export function checkScope(files) {
  const issues = [];
  const flat = {};
  for (const [lang, { file, text }] of Object.entries(files)) {
    for (const d of findDuplicateKeys(text)) {
      issues.push({ file, key: d.key, line: d.line, message: `duplicate key "${d.key}"` });
    }
    let parsed;
    try {
      parsed = JSON.parse(text);
    } catch (error) {
      issues.push({ file, message: `invalid JSON: ${error.message}` });
      continue;
    }
    flat[lang] = flatten(parsed);
    for (const [key, value] of Object.entries(flat[lang])) {
      if (typeof value !== 'string' || value.trim() === '') {
        issues.push({ file, key, message: `empty value for "${key}"` });
      }
    }
  }
  const langs = Object.keys(flat);
  for (const lang of langs) {
    for (const other of langs) {
      if (lang === other) continue;
      for (const key of Object.keys(flat[lang])) {
        if (!(key in flat[other])) {
          issues.push({
            file: files[other].file,
            key,
            message: `missing key "${key}" (present in ${files[lang].file})`,
          });
        } else if (lang < other) {
          const a = interpolationParams(flat[lang][key]).join(',');
          const b = interpolationParams(flat[other][key]).join(',');
          if (a !== b) {
            issues.push({
              file: files[other].file,
              key,
              message: `interpolation mismatch for "${key}": {${a}} vs {${b}}`,
            });
          }
        }
      }
    }
  }
  return issues;
}
