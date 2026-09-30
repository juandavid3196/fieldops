import { CatalogUsage, CsvFile } from '../models/catalog.model';

export const MAX_MONEY_CENTS = 99_999_999_999_999; // 999,999,999,999.99

/** Money formatter for the BR-05 currency; `null` symbol/formatting falls back to a plain number. */
export interface MoneyFormat {
  readonly currency: string | null;
  readonly symbol: string;
  readonly format: (value: number) => string;
}

export function moneyFormat(currency: string | null): MoneyFormat {
  const plain = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  if (currency !== null) {
    try {
      const formatter = new Intl.NumberFormat('en-US', { style: 'currency', currency });
      const symbol = formatter.formatToParts(0).find((part) => part.type === 'currency')?.value;
      if (symbol !== undefined) {
        return { currency, symbol, format: (value) => formatter.format(value) };
      }
    } catch {
      // Unknown ISO code: show the amount without a symbol.
    }
  }
  return { currency: null, symbol: '', format: (value) => plain.format(value) };
}

/** Whole cents of a money text (at most two decimals, 0 to 999,999,999,999.99); `null` when invalid. */
export function parseMoneyCents(text: string): number | null {
  const value = text.trim();
  if (!/^\d{1,12}(\.\d{1,2})?$/.test(value)) {
    return null;
  }
  const [whole, fraction = ''] = value.split('.');
  return Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
}

/** BR-09: (price - cost) / price x 100, half away from zero to one decimal; price 0 gives `null`. */
export function marginPercent(costCents: number, priceCents: number): number | null {
  if (priceCents === 0) {
    return null;
  }
  const numerator = BigInt(priceCents - costCents) * 1000n;
  const denominator = BigInt(priceCents);
  const negative = numerator < 0n;
  const absolute = negative ? -numerator : numerator;
  const tenths = (absolute * 2n + denominator) / (2n * denominator);
  return ((negative ? -1 : 1) * Number(tenths)) / 10;
}

export function marginText(percent: number | null): string {
  return percent === null ? '—' : `${percent.toFixed(1)}%`;
}

/** Collapses internal whitespace and trims (BR-07). */
export function normalizeName(value: string): string {
  return value.trim().replace(/\s+/g, ' ');
}

/** BR-10: "Used on {q} quotes, {j} jobs, and {i} invoices." with singular forms for 1. */
export function usageText(usage: CatalogUsage): string {
  const plural = (count: number, noun: string): string =>
    `${count} ${noun}${count === 1 ? '' : 's'}`;
  return `Used on ${plural(usage.quotes, 'quote')}, ${plural(usage.jobs, 'job')}, and ${plural(usage.invoices, 'invoice')}.`;
}

export const IMAGE_TYPE_MESSAGE = 'Choose a PNG or JPG image.';
export const IMAGE_SIZE_MESSAGE = 'Choose an image of 5 MB or smaller.';
export const IMAGE_MAX_BYTES = 5_242_880;
export const IMAGE_ACCEPT = '.png,.jpg,.jpeg,image/png,image/jpeg';
export const CSV_MAX_BYTES = 1_048_576;
export const CSV_SIZE_MESSAGE = 'Choose a CSV file of 1 MB or smaller.';
export const CSV_TYPE_MESSAGE = 'Choose a CSV file.';

/** Client mirror of BR-11 (declared type/extension, then size); the server decides from content. */
export function validateImageFile(file: {
  readonly name: string;
  readonly size: number;
  readonly type: string;
}): string | null {
  const extension = file.name.includes('.') ? file.name.split('.').pop()!.toLowerCase() : '';
  const typeAllowed =
    file.type === '' || ['image/png', 'image/jpeg'].includes(file.type.toLowerCase());
  if (!['png', 'jpg', 'jpeg'].includes(extension) || !typeAllowed) {
    return IMAGE_TYPE_MESSAGE;
  }
  return file.size > IMAGE_MAX_BYTES ? IMAGE_SIZE_MESSAGE : null;
}

/** Client mirror of the BR-13 size/type checks. */
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
