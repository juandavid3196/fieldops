import { UserRow } from '../models/users.model';

export type DisplayStatus = 'active' | 'suspended' | 'pending';

export function fullName(row: Pick<UserRow, 'firstName' | 'lastName'>): string {
  return `${row.firstName} ${row.lastName}`.trim();
}

/** AS-04: first letters of first and last name. */
export function initials(row: Pick<UserRow, 'firstName' | 'lastName'>): string {
  return `${row.firstName.trim().charAt(0)}${row.lastName.trim().charAt(0)}`.toUpperCase();
}

/** BR-02: an open invitation is always "Pending invitation" (expired only adds a note). */
export function displayStatus(row: Pick<UserRow, 'kind' | 'status'>): DisplayStatus {
  if (row.kind === 'invitation') {
    return 'pending';
  }
  return row.status === 'suspended' ? 'suspended' : 'active';
}

export const STATUS_LABELS: Readonly<Record<DisplayStatus, string>> = {
  active: 'Active',
  suspended: 'Suspended',
  pending: 'Pending invitation',
};

/** BR-04: "Now", "{n} min ago", "{n} hr ago", "{n} days ago" up to 30 days, then a date; "Never" when null. */
export function lastActiveText(value: string | null, now: number = Date.now()): string {
  if (value === null) {
    return 'Never';
  }
  const time = Date.parse(value);
  if (Number.isNaN(time)) {
    return 'Never';
  }
  const minutes = Math.floor(Math.max(0, now - time) / 60_000);
  if (minutes < 1) {
    return 'Now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} hr ago`;
  }
  const days = Math.floor(hours / 24);
  if (days <= 30) {
    return `${days} ${days === 1 ? 'day' : 'days'} ago`;
  }
  return new Date(time).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

/** Team profile column text (BR-04). */
export function teamProfileText(row: Pick<UserRow, 'teamProfile'>): string {
  if (!row.teamProfile.applicable) {
    return 'Not applicable';
  }
  return row.teamProfile.name ?? 'Not linked';
}
