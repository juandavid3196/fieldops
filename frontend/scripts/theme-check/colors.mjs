/**
 * Pure color-literal detection for `theme:check` (app-dark-mode BR-18). Every function returns
 * `[{ line, value }]` for the color literals found in a source text; `line` is 1-based.
 */

const NAMED = [
  'aliceblue',
  'antiquewhite',
  'aqua',
  'aquamarine',
  'azure',
  'beige',
  'bisque',
  'black',
  'blanchedalmond',
  'blue',
  'blueviolet',
  'brown',
  'burlywood',
  'cadetblue',
  'chartreuse',
  'chocolate',
  'coral',
  'cornflowerblue',
  'cornsilk',
  'crimson',
  'cyan',
  'darkblue',
  'darkcyan',
  'darkgoldenrod',
  'darkgray',
  'darkgreen',
  'darkgrey',
  'darkkhaki',
  'darkmagenta',
  'darkolivegreen',
  'darkorange',
  'darkorchid',
  'darkred',
  'darksalmon',
  'darkseagreen',
  'darkslateblue',
  'darkslategray',
  'darkslategrey',
  'darkturquoise',
  'darkviolet',
  'deeppink',
  'deepskyblue',
  'dimgray',
  'dimgrey',
  'dodgerblue',
  'firebrick',
  'floralwhite',
  'forestgreen',
  'fuchsia',
  'gainsboro',
  'ghostwhite',
  'gold',
  'goldenrod',
  'gray',
  'green',
  'greenyellow',
  'grey',
  'honeydew',
  'hotpink',
  'indianred',
  'indigo',
  'ivory',
  'khaki',
  'lavender',
  'lavenderblush',
  'lawngreen',
  'lemonchiffon',
  'lightblue',
  'lightcoral',
  'lightcyan',
  'lightgoldenrodyellow',
  'lightgray',
  'lightgreen',
  'lightgrey',
  'lightpink',
  'lightsalmon',
  'lightseagreen',
  'lightskyblue',
  'lightslategray',
  'lightslategrey',
  'lightsteelblue',
  'lightyellow',
  'lime',
  'limegreen',
  'linen',
  'magenta',
  'maroon',
  'mediumaquamarine',
  'mediumblue',
  'mediumorchid',
  'mediumpurple',
  'mediumseagreen',
  'mediumslateblue',
  'mediumspringgreen',
  'mediumturquoise',
  'mediumvioletred',
  'midnightblue',
  'mintcream',
  'mistyrose',
  'moccasin',
  'navajowhite',
  'navy',
  'oldlace',
  'olive',
  'olivedrab',
  'orange',
  'orangered',
  'orchid',
  'palegoldenrod',
  'palegreen',
  'paleturquoise',
  'palevioletred',
  'papayawhip',
  'peachpuff',
  'peru',
  'pink',
  'plum',
  'powderblue',
  'purple',
  'rebeccapurple',
  'red',
  'rosybrown',
  'royalblue',
  'saddlebrown',
  'salmon',
  'sandybrown',
  'seagreen',
  'seashell',
  'sienna',
  'silver',
  'skyblue',
  'slateblue',
  'slategray',
  'slategrey',
  'snow',
  'springgreen',
  'steelblue',
  'tan',
  'teal',
  'thistle',
  'tomato',
  'turquoise',
  'violet',
  'wheat',
  'white',
  'whitesmoke',
  'yellow',
  'yellowgreen',
];

export const NAMED_COLORS = new Set(NAMED);

const HEX =
  /(?<![\w&#-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})(?![\w-])/g;
const FUNCTION = /(?<![\w-])(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch)\(/gi;
const WORD = /(?<![\w-])[a-zA-Z]+(?![\w-])/g;

const lineOf = (text, offset) => text.slice(0, offset).split('\n').length;

/** Hex and color functions anywhere in `text`. */
export function findLiterals(text) {
  const found = [];
  for (const match of text.matchAll(HEX)) {
    found.push({ line: lineOf(text, match.index), value: match[0].toLowerCase() });
  }
  for (const match of text.matchAll(FUNCTION)) {
    found.push({ line: lineOf(text, match.index), value: callAt(text, match.index).toLowerCase() });
  }
  return found;
}

/** The full function call starting at `start` (balanced parentheses), whitespace collapsed. */
function callAt(text, start) {
  let depth = 0;
  for (let i = text.indexOf('(', start); i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')' && --depth === 0) {
      return text.slice(start, i + 1).replace(/\s+/g, ' ');
    }
  }
  return text.slice(start).replace(/\s+/g, ' ');
}

/** CSS named colors as whole words of `text` (CSS values only). */
export function findNamedColors(text) {
  const found = [];
  for (const match of text.matchAll(WORD)) {
    const word = match[0].toLowerCase();
    if (NAMED_COLORS.has(word)) {
      found.push({ line: lineOf(text, match.index), value: word });
    }
  }
  return found;
}

/** Replaces comments with spaces (newlines kept) so line numbers stay exact. */
export function stripStyleComments(text) {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(
      /(^|[\s;{}])\/\/[^\n]*/g,
      (m, lead) => lead + m.slice(lead.length).replace(/[^\n]/g, ' '),
    );
}

const COLOR_PROPERTY =
  /^(?:--[\w-]+|(?:[\w-]*color|background[\w-]*|border[\w-]*|outline[\w-]*|fill|stroke|[\w-]*shadow|text-decoration[\w-]*|caret[\w-]*|accent[\w-]*|filter|mask[\w-]*|column-rule[\w-]*|stop-[\w-]+|flood-[\w-]+))$/;

/** Literals in a `.scss`/`.css` source: hex/functions in any value, named colors in color values. */
export function scanStyle(source) {
  const text = stripStyleComments(source);
  const found = findLiterals(text);
  for (const declaration of text.matchAll(/([\w-]+)\s*:\s*([^;{}]+)/g)) {
    if (!COLOR_PROPERTY.test(declaration[1])) continue;
    const valueStart = declaration.index + declaration[0].length - declaration[2].length;
    const value = declaration[2].replace(/url\([^)]*\)/g, (m) => m.replace(/./g, ' '));
    for (const named of findNamedColors(value)) {
      found.push({ line: lineOf(text, valueStart) + named.line - 1, value: named.value });
    }
  }
  return dedupe(found);
}

const HTML_COLOR_ATTRIBUTES = new Set([
  'style',
  'fill',
  'stroke',
  'color',
  'bgcolor',
  'stop-color',
  'flood-color',
  'lighting-color',
]);
const TAG = /<[a-zA-Z][\w-]*(?:"[^"]*"|'[^']*'|[^'">])*>/g;
const ATTRIBUTE = /([\[(]?[\w.:-]+[\])]?)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
const STRING_LITERAL = /'([^']*)'|"([^"]*)"|`([^`]*)`/g;

function isColorAttribute(name) {
  if (HTML_COLOR_ATTRIBUTES.has(name)) return 'static';
  const bound = /^\[(?:style\.([\w-]+)|attr\.([\w-]+))\]$/.exec(name);
  if (!bound) return null;
  const property = bound[1] ?? bound[2];
  return property === 'style' ||
    HTML_COLOR_ATTRIBUTES.has(property) ||
    COLOR_PROPERTY.test(property)
    ? 'bound'
    : null;
}

/** Literals in a template: color attributes (static values and string literals of bindings). */
export function scanTemplate(source, lineOffset = 0) {
  const found = [];
  for (const tag of source.matchAll(TAG)) {
    for (const attribute of tag[0].matchAll(ATTRIBUTE)) {
      const kind = isColorAttribute(attribute[1]);
      if (!kind) continue;
      const value = attribute[2] ?? attribute[3];
      const at = tag.index + attribute.index + attribute[0].indexOf(value);
      const candidates =
        kind === 'static'
          ? [{ text: value, offset: at }]
          : [...value.matchAll(STRING_LITERAL)].map((m) => ({
              text: m[1] ?? m[2] ?? m[3] ?? '',
              offset: at + m.index + 1,
            }));
      for (const candidate of candidates) {
        for (const item of [...findLiterals(candidate.text), ...findNamedColors(candidate.text)]) {
          found.push({
            line: lineOf(source, candidate.offset) + item.line - 1 + lineOffset,
            value: item.value,
          });
        }
      }
    }
  }
  // Template text, `<style>` and `<script>` bodies: hex and functional colors (words are prose).
  const withoutTags = source.replace(TAG, (m) => m.replace(/[^\n]/g, ' '));
  for (const item of findLiterals(withoutTags).filter((i) => i.value.startsWith('#'))) {
    found.push({ line: item.line + lineOffset, value: item.value });
  }
  for (const match of withoutTags.matchAll(FUNCTION)) {
    found.push({
      line: lineOf(source, match.index) + lineOffset,
      value: callAt(withoutTags, match.index).toLowerCase(),
    });
  }
  return dedupe(found);
}

/** Color string literals of a component `.ts` source (comments skipped). */
export function scanTypeScript(source) {
  const text = source
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(
      /(^|[^:\\'"`])\/\/[^\n]*/g,
      (m, lead) => lead + m.slice(lead.length).replace(/[^\n]/g, ' '),
    );
  const found = [];
  for (const literal of text.matchAll(STRING_LITERAL)) {
    const body = literal[1] ?? literal[2] ?? literal[3] ?? '';
    if (/^(?:\.{1,2}\/|@|node:)/.test(body)) continue;
    const start = literal.index + 1;
    const items = [...findLiterals(body)];
    for (const declaration of body.matchAll(/([\w-]+)\s*:\s*([^;]+)/g)) {
      if (COLOR_PROPERTY.test(declaration[1])) items.push(...findNamedColors(declaration[2]));
    }
    for (const item of items) {
      found.push({ line: lineOf(text, start) + item.line - 1, value: item.value });
    }
  }
  // Named colors assigned to a color property (`fillStyle = 'white'`, `color: 'red'`). A bare tone
  // name such as `'teal'` used as an identifier is not a color value.
  const assigned =
    /\b(?:\w*[cC]olor|fill\w*|stroke\w*|background\w*)\s*[:=]\s*(['"`])([a-zA-Z]+)\1/g;
  for (const match of text.matchAll(assigned)) {
    const name = match[2].toLowerCase();
    if (NAMED_COLORS.has(name)) found.push({ line: lineOf(text, match.index), value: name });
  }
  return dedupe(found);
}

function dedupe(found) {
  const seen = new Set();
  return found.filter((item) => {
    const key = `${item.line}:${item.value}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/**
 * Color tokens of `_tokens.scss`: every custom property whose value has a color literal must be a
 * `light-dark()` pair (or be allowlisted). Returns `[{ line, token }]` for the offenders.
 */
export function scanTokens(source) {
  const text = stripStyleComments(source);
  const offenders = [];
  for (const declaration of text.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
    const value = declaration[2];
    if (findLiterals(value).length > 0 && !/light-dark\(/i.test(value)) {
      offenders.push({ line: lineOf(text, declaration.index), token: declaration[1] });
    }
  }
  return offenders;
}
