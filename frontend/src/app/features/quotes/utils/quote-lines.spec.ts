import { CatalogRow } from '../../products-services/models/catalog.model';
import { Calculation, DraftBody, QuoteDetail } from '../models/quote.model';
import { previewView, relativeTime } from './quote-format';
import {
  DraftState,
  QuoteLine,
  addLine,
  canMove,
  customLine,
  draftErrors,
  emailMessageError,
  lineErrors,
  lineFromCatalog,
  moveLine,
  moveWithinSection,
  toDraftBody,
} from './quote-lines';

const line = (uid: string, isOptional = false, patch: Partial<QuoteLine> = {}): QuoteLine => ({
  ...customLine('service', isOptional, uid),
  name: uid,
  ...patch,
});
const names = (lines: readonly QuoteLine[]): string[] => lines.map((l) => l.uid);

const state = (patch: Partial<DraftState> = {}): DraftState => ({
  lines: [line('a')],
  discountTotal: 0,
  customerMessage: '',
  internalNote: '',
  terms: { preset: 'due_on_completion', customText: '' },
  validUntil: '2026-11-04',
  ...patch,
});

describe('quote line helpers', () => {
  it('reorders inside one section only: move up/down, drag between positions, section ends (quote-builder BR-12, AC-07)', () => {
    // 3 regular lines then 2 optional lines (BR-11).
    const lines = [line('a'), line('b'), line('c'), line('x', true), line('y', true)];

    expect(names(moveWithinSection(lines, 1, -1)!.lines)).toEqual(['b', 'a', 'c', 'x', 'y']);
    expect(names(moveWithinSection(lines, 0, 1)!.lines)).toEqual(['b', 'a', 'c', 'x', 'y']);
    expect(names(moveWithinSection(lines, 3, 1)!.lines)).toEqual(['a', 'b', 'c', 'y', 'x']);
    // The announcement data: position inside its own section.
    expect(moveWithinSection(lines, 3, 1)).toMatchObject({ position: 2, count: 2 });
    expect(moveLine(lines, 0, 2)).toMatchObject({ position: 3, count: 3 });

    // Never crosses a section, never leaves the list, never moves onto itself.
    expect(moveWithinSection(lines, 2, 1)).toBeNull();
    expect(moveWithinSection(lines, 3, -1)).toBeNull();
    expect(moveWithinSection(lines, 0, -1)).toBeNull();
    expect(moveWithinSection(lines, 4, 1)).toBeNull();
    expect(moveLine(lines, 0, 3)).toBeNull();
    expect(moveLine(lines, 1, 1)).toBeNull();
    expect([canMove(lines, 2, 1), canMove(lines, 2, -1), canMove(lines, 3, -1)]).toEqual([
      false,
      true,
      false,
    ]);

    // New lines join their own section: regular before the optional ones, optional last.
    expect(names(addLine(lines, line('n')))).toEqual(['a', 'b', 'c', 'n', 'x', 'y']);
    expect(names(addLine(lines, line('o', true)))).toEqual(['a', 'b', 'c', 'x', 'y', 'o']);
  });

  it('builds lines from the catalog or as custom lines and strips client-only fields from the body (quote-builder BR-09, BR-22, AC-06)', () => {
    const item = {
      id: 'item-1',
      type: 'product',
      name: 'Shutoff valve',
      description: null,
      unitCost: 12.5,
      unitPrice: 42,
      isTaxable: false,
    } as CatalogRow;

    expect(lineFromCatalog(item, 'product', true, 'u1')).toEqual({
      uid: 'u1',
      catalogItemId: 'item-1',
      type: 'product',
      name: 'Shutoff valve',
      description: '',
      quantity: 1,
      unit: 'unit',
      unitPrice: 42,
      unitCost: 12.5,
      taxable: false,
      isOptional: true,
    });
    expect(customLine('service', false, 'u2')).toMatchObject({
      catalogItemId: null,
      name: '',
      unit: 'unit',
      unitPrice: 0,
      unitCost: 0,
      taxable: true,
    });

    // Regular lines first, texts trimmed, empty texts null, terms text only for Custom, no `uid`.
    const body = toDraftBody(
      state({
        lines: [
          line('o', true, { name: '  Extra   work ' }),
          line('r', false, { description: ' d ' }),
        ],
        discountTotal: null,
        customerMessage: '  hi ',
        internalNote: '   ',
        terms: { preset: 'net_30', customText: 'ignored' },
      }),
    );
    expect(body.lines.map((l) => l.name)).toEqual(['r', 'Extra work']);
    expect(body.lines[0]).not.toHaveProperty('uid');
    expect(body).toMatchObject({
      discountTotal: 0,
      customerMessage: 'hi',
      internalNote: null,
      terms: { preset: 'net_30' },
    });
    expect(body.terms).not.toHaveProperty('customText');
    expect(
      toDraftBody(state({ terms: { preset: 'custom', customText: ' Pay now ' } })).terms,
    ).toEqual({ preset: 'custom', customText: 'Pay now' });
  });

  it.each<[string, Partial<QuoteLine>, string | undefined]>([
    ['empty name', { name: '   ' }, 'name'],
    ['name over 160', { name: 'n'.repeat(161) }, 'name'],
    ['description over 1000', { description: 'd'.repeat(1001) }, 'description'],
    ['zero quantity', { quantity: 0 }, 'quantity'],
    ['blank quantity', { quantity: null }, 'quantity'],
    ['quantity over the limit', { quantity: 1_000_000 }, 'quantity'],
    ['4 decimals quantity', { quantity: 1.2345 }, 'quantity'],
    ['empty unit', { unit: ' ' }, 'unit'],
    ['unit over 40', { unit: 'u'.repeat(41) }, 'unit'],
    ['negative price', { unitPrice: -1 }, 'unitPrice'],
    ['3 decimals price', { unitPrice: 1.005 }, 'unitPrice'],
    ['price over the limit', { unitPrice: 1_000_000_000 }, 'unitPrice'],
    ['negative cost', { unitCost: -0.01 }, 'unitCost'],
    [
      'valid edge values',
      { quantity: 999_999.999, unitPrice: 999_999_999.99, unit: 'hr' },
      undefined,
    ],
  ])('validates a line per BR-10: %s', (_case, patch, field) => {
    const errors = lineErrors(line('a', false, patch));

    expect(Object.keys(errors)).toEqual(field === undefined ? [] : [field]);
  });

  it('validates the draft fields and the e-mail message with the spec messages (quote-builder BR-14, BR-17 to BR-19, BR-21)', () => {
    expect(draftErrors(state())).toEqual({});
    expect(
      draftErrors(
        state({
          lines: [line('a', false, { name: '' })],
          discountTotal: -1,
          customerMessage: 'm'.repeat(501),
          internalNote: 'n'.repeat(501),
          terms: { preset: 'custom', customText: '' },
          validUntil: '',
        }),
      ),
    ).toEqual({
      'a.name': 'Enter an item name.',
      discountTotal: 'Enter a discount of 0 or more.',
      customerMessage: 'Customer message must be 500 characters or fewer.',
      internalNote: 'Internal note must be 500 characters or fewer.',
      'terms.customText': 'Enter the terms.',
      validUntil: 'Choose a date between tomorrow and one year from today.',
    });
    expect(draftErrors(state({ discountTotal: 1.234 }))['discountTotal']).toBeDefined();
    expect(
      draftErrors(state({ lines: Array.from({ length: 101 }, (_, i) => line(`l${i}`)) }))['lines'],
    ).toBe('Add up to 100 lines.');
    expect([
      emailMessageError(' '),
      emailMessageError('m'.repeat(321)),
      emailMessageError('ok'),
    ]).toEqual(['Enter a message.', 'Message must be 320 characters or fewer.', null]);
  });
});

describe('quote formatting', () => {
  it('builds the customer preview without any internal data and formats the save time (quote-builder BR-23, AC-16)', () => {
    const detail = {
      displayNumber: 'Q-2036',
      organization: { name: 'Northstar', currency: 'USD', defaultTaxRate: 8.25 },
      customer: { name: 'Sofia Martinez', address: '1842 Oak Street' },
      request: { title: 'Kitchen sink leak' },
    } as QuoteDetail;
    const body: DraftBody = {
      lines: [
        {
          catalogItemId: null,
          type: 'service',
          name: 'Labor',
          description: 'Fix it',
          quantity: 2,
          unit: 'hr',
          unitPrice: 95,
          unitCost: 40,
          taxable: true,
          isOptional: false,
        },
        {
          catalogItemId: null,
          type: 'product',
          name: 'Valve',
          description: '',
          quantity: 1,
          unit: 'unit',
          unitPrice: 120,
          unitCost: 70,
          taxable: true,
          isOptional: true,
        },
      ],
      discountTotal: 10,
      customerMessage: 'Hello',
      internalNote: 'SECRET NOTE',
      terms: { preset: 'net_30' },
      validUntil: '2026-11-04',
    };
    const calc: Calculation = {
      lines: [
        { lineSubtotal: 190, discountShare: 10, taxRate: 8.25, lineTax: 14.85, lineTotal: 194.85 },
        { lineSubtotal: 120, discountShare: 0, taxRate: 8.25, lineTax: 9.9, lineTotal: 129.9 },
      ],
      subtotal: 190,
      discountTotal: 10,
      taxLabel: 'Tax (8.25%)',
      taxTotal: 14.85,
      total: 194.85,
      currency: 'USD',
      margin: { percent: 65.4, grossProfit: 113.33 },
    };

    const view = previewView(detail, body, calc, 1, new Date('2026-10-05T12:00:00Z'));

    expect(view).toMatchObject({
      number: 'Q-2036',
      scope: 'Kitchen sink leak',
      terms: 'Payment due within 30 days of the invoice date.',
      validUntil: 'Nov 4, 2026',
      subtotal: 190,
      total: 194.85,
    });
    expect(view.lines.map((l) => [l.name, l.amount])).toEqual([['Labor', 190]]);
    expect(view.optionalLines.map((l) => [l.name, l.amount])).toEqual([['Valve', 120]]);
    const serialized = JSON.stringify(view);
    for (const secret of ['SECRET NOTE', 'unitCost', 'margin', '113.33', 'diagnosis']) {
      expect(serialized).not.toContain(secret);
    }

    const now = new Date('2026-10-05T12:00:00Z');
    expect([
      relativeTime('2026-10-05T11:59:30Z', now, 'UTC'),
      relativeTime('2026-10-05T11:55:00Z', now, 'UTC'),
      relativeTime('2026-10-05T09:00:00Z', now, 'UTC'),
    ]).toEqual(['Just now', '5 min ago', '3 h ago']);
  });
});
