/** Reads a dotted path (`branch.code`) from a form value snapshot; `undefined` when absent. */
export function valueAtPath(source: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>(
      (current, key) =>
        current !== null && typeof current === 'object'
          ? (current as Record<string, unknown>)[key]
          : undefined,
      source,
    );
}

/**
 * Dotted keys of the fields whose value differs between two form value snapshots. Used to
 * revalidate only the fields the user is editing, leaving other messages (for example, server
 * errors on untouched fields) in place.
 */
export function changedKeys(
  keys: readonly string[],
  before: unknown,
  after: unknown,
): readonly string[] {
  return keys.filter((key) => valueAtPath(before, key) !== valueAtPath(after, key));
}
