import { CsvFile, DisplayStatus } from '../models/customer.model';

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE_CHARACTERS = /^\+?[\d\s().-]*$/;

/** Trimmed, lower-cased email when syntactically valid (BR-10), else `null`. */
export function normalizeEmail(value: string): string | null {
  const email = value.trim().toLowerCase();
  return email.length > 0 && email.length <= 254 && EMAIL_PATTERN.test(email) ? email : null;
}

/** BR-10 phone: digits only, 10-15 digits, an 11-digit `1` number keeps its last 10; else `null`. */
export function normalizePhone(value: string): string | null {
  const text = value.trim();
  if (text.length === 0 || !PHONE_CHARACTERS.test(text)) {
    return null;
  }
  const digits = text.replace(/\D/g, '');
  if (digits.length < 10 || digits.length > 15) {
    return null;
  }
  return digits.length === 11 && digits.startsWith('1') ? digits.slice(1) : digits;
}

/** 10 digits display as "(512) 555-7832"; anything else is shown as stored. */
export function formatPhone(value: string | null | undefined): string {
  const digits = (value ?? '').trim();
  return /^\d{10}$/.test(digits)
    ? `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`
    : digits;
}

export interface MoneyFormat {
  readonly format: (value: number) => string;
}

/** BR-05: organization currency, no decimals when whole, otherwise two. */
export function moneyFormat(currency: string | null): MoneyFormat {
  const build = (fraction: number): Intl.NumberFormat => {
    const options = { minimumFractionDigits: fraction, maximumFractionDigits: fraction };
    try {
      return currency === null
        ? new Intl.NumberFormat('en-US', options)
        : new Intl.NumberFormat('en-US', { style: 'currency', currency, ...options });
    } catch {
      return new Intl.NumberFormat('en-US', options);
    }
  };
  const whole = build(0);
  const cents = build(2);
  return { format: (value) => (Number.isInteger(value) ? whole : cents).format(value) };
}

function formatterParts(
  iso: string,
  timeZone: string,
  options: Intl.DateTimeFormatOptions,
): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const resolved = (zone: string): string =>
    new Intl.DateTimeFormat('en-US', { ...options, timeZone: zone })
      .format(date)
      .replace(/[\u202f\u00a0]/g, ' ');
  try {
    return resolved(timeZone);
  } catch {
    return resolved('UTC');
  }
}

/** "MMM d, yyyy" of a UTC instant in the organization timezone (BR-09). */
export function formatDate(iso: string, timeZone: string): string {
  return formatterParts(iso, timeZone, { month: 'short', day: 'numeric', year: 'numeric' });
}

/** "h:mm a" of a UTC instant in the organization timezone (BR-09). */
export function formatTime(iso: string, timeZone: string): string {
  return formatterParts(iso, timeZone, { hour: 'numeric', minute: '2-digit', hour12: true });
}

export const STATUS_LABELS: Readonly<Record<DisplayStatus, string>> = {
  active: 'Active',
  lead: 'Lead',
  overdue: 'Overdue',
  archived: 'Archived',
};

/** BR-04 tones: Active green, Overdue red, Lead and Archived grey. */
export function statusSeverity(status: DisplayStatus): 'success' | 'danger' | 'secondary' {
  return status === 'active' ? 'success' : status === 'overdue' ? 'danger' : 'secondary';
}

export function propertyCountText(count: number): string {
  return `${count} ${count === 1 ? 'property' : 'properties'}`;
}

/** Page numbers with gaps (`null`) for the paginator: first pages, neighbours and the last page. */
export function pageItems(page: number, totalPages: number): readonly (number | null)[] {
  const wanted = new Set<number>([1, page - 1, page, page + 1, totalPages]);
  if (page <= 2) {
    wanted.add(2);
    wanted.add(3);
  }
  const pages = [...wanted].filter((n) => n >= 1 && n <= totalPages).sort((a, b) => a - b);
  const items: (number | null)[] = [];
  pages.forEach((n, index) => {
    const previous = index === 0 ? null : pages[index - 1];
    if (previous !== null && n - previous === 2) {
      items.push(previous + 1);
    } else if (previous !== null && n - previous > 2) {
      items.push(null);
    }
    items.push(n);
  });
  return items;
}

export const CSV_MAX_BYTES = 1_048_576;
export const CSV_SIZE_MESSAGE = 'Choose a CSV file of 1 MB or smaller.';
export const CSV_TYPE_MESSAGE = 'Choose a CSV file.';

/** Client mirror of the BR-17 extension and size checks; the server decides from content. */
export function validateCsvFile(file: {
  readonly name: string;
  readonly size: number;
}): string | null {
  if (!file.name.toLowerCase().endsWith('.csv')) {
    return CSV_TYPE_MESSAGE;
  }
  return file.size > CSV_MAX_BYTES ? CSV_SIZE_MESSAGE : null;
}

/** Saves a downloaded CSV through a temporary object URL. */
export function saveCsv(file: CsvFile): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
