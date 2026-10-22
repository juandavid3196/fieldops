/**
 * Hardcoded template text check (BR-12): a static text node with letters, or a static
 * `aria-label`, `aria-description`, `placeholder`, `title`, `alt` or `label` attribute, must be in
 * the versioned allowlist. Bindings, translation pipes, icons and allowlisted entries pass.
 */
const LETTERS = /\p{L}/u;
const ATTRIBUTES = new Set([
  'aria-label',
  'aria-description',
  'placeholder',
  'title',
  'alt',
  'label',
  'ariaLabel',
  'pTooltip',
  'data-label',
]);
const CHILDREN = [
  'children',
  'branches',
  'cases',
  'groups',
  'empty',
  'placeholder',
  'loading',
  'error',
  'defaultCase',
];

function walk(node, visit) {
  if (!node || typeof node !== 'object') return;
  visit(node);
  for (const key of CHILDREN) {
    const value = node[key];
    if (Array.isArray(value)) value.forEach((child) => walk(child, visit));
    else if (value) walk(value, visit);
  }
}

const normalize = (value) => value.replace(/\s+/g, ' ').trim();

/**
 * `parseTemplate` is injected (from `@angular/compiler`). Returns `[{ file, line, text?, attr?, message }]`.
 */
export function checkTemplate({ source, file, allowlist, parseTemplate, lineOffset = 0 }) {
  const issues = [];
  const allowed = (entry) =>
    allowlist.some(
      (item) =>
        item.file === file &&
        ((entry.text !== undefined && item.text === entry.text) ||
          (entry.attr !== undefined && item.attr === entry.attr && item.value === entry.value)),
    );
  const result = parseTemplate(source, file, { preserveWhitespaces: true });
  if (result.errors?.length) {
    return [{ file, line: 1, message: `template parse error: ${result.errors[0].msg}` }];
  }
  const lineOf = (offset) => source.slice(0, offset).split('\n').length + lineOffset;
  for (const root of result.nodes) {
    walk(root, (node) => {
      const type = node.constructor.name;
      if (type === 'Text' && LETTERS.test(node.value)) {
        const text = normalize(node.value);
        if (!allowed({ text })) {
          issues.push({
            file,
            line: lineOf(node.sourceSpan.start.offset),
            text,
            message: `hardcoded text "${text}"`,
          });
        }
      }
      if (type === 'Element') {
        for (const attribute of node.attributes ?? []) {
          if (ATTRIBUTES.has(attribute.name) && LETTERS.test(attribute.value)) {
            const value = normalize(attribute.value);
            if (!allowed({ attr: attribute.name, value })) {
              issues.push({
                file,
                line: lineOf(attribute.sourceSpan.start.offset),
                attr: attribute.name,
                message: `hardcoded ${attribute.name}="${value}"`,
              });
            }
          }
        }
      }
    });
  }
  return issues;
}

/** Extracts inline `template: \`...\`` bodies of a TypeScript source: `[{ source, lineOffset }]`. */
export function inlineTemplates(tsSource) {
  const found = [];
  for (const match of tsSource.matchAll(/template:\s*`([\s\S]*?)`/g)) {
    const start = match.index + match[0].indexOf('`') + 1;
    found.push({ source: match[1], lineOffset: tsSource.slice(0, start).split('\n').length - 1 });
  }
  return found;
}
