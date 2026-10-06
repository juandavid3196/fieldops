import { CatalogRow } from '../../products-services/models/catalog.model';
import {
  DraftBody,
  DraftLine,
  LineType,
  TermsBody,
  TermsPreset,
  QuoteDraft,
} from '../models/quote.model';

/** A line in the editor: the contract fields (numbers may be blank while typing) plus a client-only `uid`. */
export interface QuoteLine {
  readonly uid: string;
  readonly catalogItemId: string | null;
  readonly type: LineType;
  readonly name: string;
  readonly description: string;
  readonly quantity: number | null;
  readonly unit: string;
  readonly unitPrice: number | null;
  readonly unitCost: number | null;
  readonly taxable: boolean;
  readonly isOptional: boolean;
}

export type LineField = 'name' | 'description' | 'quantity' | 'unit' | 'unitPrice' | 'unitCost';
export type LineErrors = Partial<Record<LineField, string>>;

export interface TermsState {
  readonly preset: TermsPreset;
  readonly customText: string;
}

export interface DraftState {
  readonly lines: readonly QuoteLine[];
  readonly discountTotal: number | null;
  readonly customerMessage: string;
  readonly internalNote: string;
  readonly terms: TermsState;
  readonly validUntil: string;
}

export const MAX_LINES = 100;
export const MAX_MONEY = 999_999_999.99;
export const MAX_QUANTITY = 999_999.999;
export const MESSAGE_LIMIT = 500;
export const TERMS_LIMIT = 2000;
export const EMAIL_LIMIT = 320;

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

function collapse(value: string): string {
  return value.replace(/\s+/g, ' ').trim();
}

/** True when `value` has at most `places` decimals (tolerant of binary rounding). */
export function hasAtMostDecimals(value: number, places: number): boolean {
  const scaled = value * 10 ** places;
  return Math.abs(scaled - Math.round(scaled)) < 1e-6;
}

function money(value: number | null): boolean {
  return (
    value !== null &&
    Number.isFinite(value) &&
    value >= 0 &&
    value <= MAX_MONEY &&
    hasAtMostDecimals(value, 2)
  );
}

/** BR-10 limits and messages for one line; empty when the line is valid. */
export function lineErrors(line: QuoteLine): LineErrors {
  const errors: Record<string, string> = {};
  const name = collapse(line.name);
  if (name.length === 0) {
    errors['name'] = 'Enter an item name.';
  } else if (name.length > 160) {
    errors['name'] = 'Item name must be 160 characters or fewer.';
  }
  if (line.description.trim().length > 1000) {
    errors['description'] = 'Description must be 1,000 characters or fewer.';
  }
  const quantity = line.quantity;
  if (
    quantity === null ||
    !Number.isFinite(quantity) ||
    quantity <= 0 ||
    quantity > MAX_QUANTITY ||
    !hasAtMostDecimals(quantity, 3)
  ) {
    errors['quantity'] = 'Enter a quantity greater than 0.';
  }
  const unit = line.unit.trim();
  if (unit.length === 0 || unit.length > 40) {
    errors['unit'] = 'Enter a unit.';
  }
  if (!money(line.unitPrice)) {
    errors['unitPrice'] = 'Enter a price of 0 or more.';
  }
  if (!money(line.unitCost)) {
    errors['unitCost'] = 'Enter a cost of 0 or more.';
  }
  return errors;
}

export function isLineValid(line: QuoteLine): boolean {
  return Object.keys(lineErrors(line)).length === 0;
}

/** Field errors of the whole draft, keyed `<uid>.<field>` for lines (BR-10, BR-14, BR-17 to BR-19). */
export function draftErrors(state: DraftState): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const line of state.lines) {
    for (const [field, message] of Object.entries(lineErrors(line))) {
      errors[`${line.uid}.${field}`] = message;
    }
  }
  if (state.lines.length > MAX_LINES) {
    errors['lines'] = `Add up to ${MAX_LINES} lines.`;
  }
  const discount = state.discountTotal;
  if (
    discount === null ||
    !Number.isFinite(discount) ||
    discount < 0 ||
    !hasAtMostDecimals(discount, 2)
  ) {
    errors['discountTotal'] = 'Enter a discount of 0 or more.';
  }
  if (state.customerMessage.trim().length > MESSAGE_LIMIT) {
    errors['customerMessage'] = 'Customer message must be 500 characters or fewer.';
  }
  if (state.internalNote.trim().length > MESSAGE_LIMIT) {
    errors['internalNote'] = 'Internal note must be 500 characters or fewer.';
  }
  if (state.terms.preset === 'custom') {
    const text = state.terms.customText.trim();
    if (text.length === 0) {
      errors['terms.customText'] = 'Enter the terms.';
    } else if (text.length > TERMS_LIMIT) {
      errors['terms.customText'] = 'Terms must be 2000 characters or fewer.';
    }
  }
  if (!DATE_ONLY.test(state.validUntil)) {
    errors['validUntil'] = 'Choose a date between tomorrow and one year from today.';
  }
  return errors;
}

/** BR-24 message check (the message is only sent, never stored). */
export function emailMessageError(message: string): string | null {
  const text = message.trim();
  if (text.length === 0) {
    return 'Enter a message.';
  }
  return text.length > EMAIL_LIMIT ? 'Message must be 320 characters or fewer.' : null;
}

/** Regular lines first, then optional lines, each section keeping its order (BR-11). */
export function orderedLines<T extends { readonly isOptional: boolean }>(lines: readonly T[]): T[] {
  return [...lines.filter((line) => !line.isOptional), ...lines.filter((line) => line.isOptional)];
}

function toDraftLine(line: QuoteLine): DraftLine {
  return {
    catalogItemId: line.catalogItemId,
    type: line.type,
    name: collapse(line.name),
    description: line.description.trim(),
    quantity: line.quantity ?? 0,
    unit: line.unit.trim(),
    unitPrice: line.unitPrice ?? 0,
    unitCost: line.unitCost ?? 0,
    taxable: line.taxable,
    isOptional: line.isOptional,
  };
}

/** The request body: client-only fields stripped, texts trimmed, empty texts as null. */
export function toDraftBody(state: DraftState): DraftBody {
  const message = state.customerMessage.trim();
  const note = state.internalNote.trim();
  const terms: TermsBody =
    state.terms.preset === 'custom'
      ? { preset: 'custom', customText: state.terms.customText.trim() }
      : { preset: state.terms.preset };
  return {
    lines: orderedLines(state.lines).map(toDraftLine),
    discountTotal: state.discountTotal ?? 0,
    customerMessage: message.length > 0 ? message : null,
    internalNote: note.length > 0 ? note : null,
    terms,
    validUntil: state.validUntil,
  };
}

/** Editor state of a loaded draft; `uid` makes the lines addressable across reorders and edits. */
export function stateFromDraft(draft: QuoteDraft, uid: () => string): DraftState {
  return {
    lines: orderedLines(draft.lines).map((line) => ({ ...line, uid: uid() })),
    discountTotal: draft.discountTotal,
    customerMessage: draft.customerMessage ?? '',
    internalNote: draft.internalNote ?? '',
    terms: { preset: draft.terms.preset, customText: draft.terms.customText ?? '' },
    validUntil: draft.validUntil,
  };
}

/** BR-09 catalog line: copies the item's snapshot fields; every one stays editable. */
export function lineFromCatalog(
  item: CatalogRow,
  type: LineType,
  isOptional: boolean,
  uid: string,
): QuoteLine {
  return {
    uid,
    catalogItemId: item.id,
    type,
    name: item.name,
    description: item.description ?? '',
    quantity: 1,
    unit: 'unit',
    unitPrice: item.unitPrice,
    unitCost: item.unitCost,
    taxable: item.isTaxable,
    isOptional,
  };
}

/** BR-09 custom line: no catalog item, empty texts, unit "unit", price and cost 0, taxable. */
export function customLine(type: LineType, isOptional: boolean, uid: string): QuoteLine {
  return {
    uid,
    catalogItemId: null,
    type,
    name: '',
    description: '',
    quantity: 1,
    unit: 'unit',
    unitPrice: 0,
    unitCost: 0,
    taxable: true,
    isOptional,
  };
}

/** A new regular line goes after the last regular line; an optional one at the end. */
export function addLine(lines: readonly QuoteLine[], line: QuoteLine): QuoteLine[] {
  return orderedLines([...lines, line]);
}

export interface Move {
  readonly lines: QuoteLine[];
  /** Position (1-based) in the line's section and the section size, for the announcement. */
  readonly position: number;
  readonly count: number;
  readonly line: QuoteLine;
}

/**
 * Moves the line at `from` to `to`. Never crosses between the regular and the optional section:
 * returns `null` for a cross-section, out-of-range or no-op move (BR-12).
 */
export function moveLine(lines: readonly QuoteLine[], from: number, to: number): Move | null {
  const line = lines[from];
  const target = lines[to];
  if (
    line === undefined ||
    target === undefined ||
    from === to ||
    line.isOptional !== target.isOptional
  ) {
    return null;
  }
  const next = [...lines];
  next.splice(from, 1);
  next.splice(to, 0, line);
  const section = next.filter((candidate) => candidate.isOptional === line.isOptional);
  return { lines: next, position: section.indexOf(line) + 1, count: section.length, line };
}

/** Move up (-1) or down (+1) inside the line's own section. */
export function moveWithinSection(
  lines: readonly QuoteLine[],
  from: number,
  direction: -1 | 1,
): Move | null {
  return moveLine(lines, from, from + direction);
}

/** Whether Move up / Move down are available for a line (disabled at section ends). */
export function canMove(lines: readonly QuoteLine[], index: number, direction: -1 | 1): boolean {
  const line = lines[index];
  const neighbour = lines[index + direction];
  return line !== undefined && neighbour !== undefined && neighbour.isOptional === line.isOptional;
}

export function lineName(line: { readonly name: string }, index: number): string {
  const name = line.name.trim();
  return name.length > 0 ? name : `Line ${index + 1}`;
}
