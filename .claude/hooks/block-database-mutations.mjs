// PreToolUse hook: blocks commands that mutate or destroy a database.
// Only executables are analyzed (the start of the command and anything after
// &&, ||, ;, |, &, newlines, parentheses or inside $(...)), so text that only
// appears in arguments such as commit messages or echo output is not blocked.

const BLOCKED_MESSAGE =
  'Blocked: database-changing commands require explicit manual execution.\n';

const DROP_DATABASE = /\bdrop\s+database\b/i;
const WRAPPERS = new Set(['sudo', 'env', 'time', 'command', 'exec', 'nohup']);
const SHELLS = new Set(['bash', 'sh', 'zsh', 'dash', 'pwsh', 'powershell', 'cmd']);
const SHELL_COMMAND_FLAGS = /^(-c|-command|\/c|\/k)$/i;
const MAX_DEPTH = 5;

function findClosingParen(text, start) {
  let depth = 1;
  let quote = null;

  for (let i = start; i < text.length; i++) {
    const ch = text[i];

    if (quote) {
      if (ch === quote) quote = null;
    } else if (ch === "'" || ch === '"') {
      quote = ch;
    } else if (ch === '(') {
      depth++;
    } else if (ch === ')' && --depth === 0) {
      return i;
    }
  }

  return text.length;
}

// Splits a command line into segments of unquoted words. Command substitutions
// $(...) are collected in `nested` so they can be analyzed as commands too.
function splitSegments(command, nested) {
  const segments = [];
  let words = [];
  let word = '';
  let inWord = false;
  let quote = null;

  const endWord = () => {
    if (inWord) words.push(word);
    word = '';
    inWord = false;
  };

  const endSegment = () => {
    endWord();
    if (words.length > 0) segments.push(words);
    words = [];
  };

  for (let i = 0; i < command.length; i++) {
    const ch = command[i];
    const next = command[i + 1];

    if (quote === "'") {
      if (ch === "'") quote = null;
      else word += ch;
      continue;
    }

    if (ch === '$' && next === '(') {
      const end = findClosingParen(command, i + 2);
      nested.push(command.slice(i + 2, end));
      inWord = true;
      i = end;
      continue;
    }

    if (quote === '"') {
      if (ch === '"') {
        quote = null;
      } else if (ch === '\\' && next === '"') {
        word += next;
        i++;
      } else {
        word += ch;
      }
      continue;
    }

    if (ch === "'" || ch === '"') {
      quote = ch;
      inWord = true;
    } else if (ch === '\\' && next !== undefined && /[\s'"`;&|()]/.test(next)) {
      // Backslashes are kept literally otherwise, so Windows paths survive.
      word += next;
      inWord = true;
      i++;
    } else if (/[;&|()\n]/.test(ch)) {
      endSegment();
    } else if (/\s/.test(ch)) {
      endWord();
    } else {
      word += ch;
      inWord = true;
    }
  }

  endSegment();
  return segments;
}

function commandName(word) {
  return word
    .split(/[\\/]/)
    .pop()
    .toLowerCase()
    .replace(/\.(exe|cmd|bat|ps1)$/, '');
}

function stripPrefixes(words) {
  let i = 0;

  while (
    i < words.length &&
    (/^[A-Za-z_][A-Za-z0-9_]*=/.test(words[i]) || WRAPPERS.has(commandName(words[i])))
  ) {
    i++;
  }

  return words.slice(i);
}

// dotnet ef [options] database update|drop [options]
function isEfDatabaseMutation(executable, args) {
  const lower = args.map((arg) => arg.toLowerCase());
  let efArgs;

  if (executable === 'dotnet-ef') {
    efArgs = lower;
  } else if (executable === 'dotnet') {
    if (lower[0] === 'ef' || lower[0] === 'dotnet-ef') {
      efArgs = lower.slice(1);
    } else if (lower[0] === 'tool' && lower[1] === 'run' && lower[2] === 'dotnet-ef') {
      efArgs = lower.slice(3);
    }
  }

  if (!efArgs) return false;

  const group = efArgs.findIndex((arg) => ['database', 'migrations', 'dbcontext'].includes(arg));

  return (
    group !== -1 &&
    efArgs[group] === 'database' &&
    efArgs.slice(group + 1).some((arg) => arg === 'update' || arg === 'drop')
  );
}

// docker compose [options] down [options] -v|--volumes, or docker-compose ...
function isComposeVolumeRemoval(executable, args) {
  let composeArgs;

  if (executable === 'docker-compose') {
    composeArgs = args;
  } else if (executable === 'docker' || executable === 'podman') {
    const compose = args.indexOf('compose');
    if (compose !== -1) composeArgs = args.slice(compose + 1);
  }

  if (!composeArgs) return false;

  const down = composeArgs.indexOf('down');

  return (
    down !== -1 &&
    composeArgs.slice(down + 1).some((arg) => arg === '-v' || /^--volumes(=(true|1))?$/i.test(arg))
  );
}

function runsPsql(executable, args) {
  if (executable === 'psql') return true;

  return (
    ['docker', 'podman', 'kubectl'].includes(executable) &&
    args.includes('exec') &&
    args.some((arg) => commandName(arg) === 'psql')
  );
}

function isForbidden(command, depth = 0) {
  if (depth > MAX_DEPTH) return false;

  const nested = [];
  const segments = splitSegments(command, nested);
  let psql = false;

  for (const words of segments) {
    const [first, ...args] = stripPrefixes(words);
    if (!first) continue;

    const executable = commandName(first);

    if (SHELLS.has(executable)) {
      const flag = args.findIndex((arg) => SHELL_COMMAND_FLAGS.test(arg));
      if (flag !== -1 && isForbidden(args.slice(flag + 1).join(' '), depth + 1)) return true;
    }

    if (
      (executable === 'iex' || executable === 'invoke-expression') &&
      isForbidden(args.join(' '), depth + 1)
    ) {
      return true;
    }

    if (isEfDatabaseMutation(executable, args) || isComposeVolumeRemoval(executable, args)) {
      return true;
    }

    if (runsPsql(executable, args)) psql = true;
  }

  // SQL can reach psql through -c, a pipe or a heredoc, so check the whole command.
  if (psql && DROP_DATABASE.test(command)) return true;

  return nested.some((inner) => isForbidden(inner, depth + 1));
}

let input = '';

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  input += chunk;
});

process.stdin.on('end', () => {
  let payload;

  try {
    // Windows PowerShell 5.1 prepends a UTF-8 BOM when piping to native commands.
    payload = JSON.parse(input.replace(/^\uFEFF/, ''));
  } catch {
    process.exit(0);
  }

  const command = String(payload?.tool_input?.command ?? '');

  if (isForbidden(command)) {
    process.stderr.write(BLOCKED_MESSAGE);
    process.exit(2);
  }

  process.exit(0);
});
