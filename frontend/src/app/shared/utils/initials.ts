/** Trimmed first and last name joined with one space; empty parts are skipped. */
export function fullNameOf(user: { firstName?: string; lastName?: string } | undefined): string {
  return [user?.firstName, user?.lastName]
    .map((part) => part?.trim() ?? '')
    .filter((part) => part !== '')
    .join(' ');
}

/** Upper-cased first letter of every word of a full name (authenticated-app-shell BR-02). */
export function initialsOf(fullName: string): string {
  return fullName
    .split(' ')
    .filter((part) => part !== '')
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}
