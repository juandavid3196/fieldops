import { WEEKDAYS, Weekday } from '../../models/organization-registration.model';
import {
  TIME_OPTIONS,
  copyMondayToWeekdays,
  numberingExamples,
  passwordRequirements,
  summarizeBusinessHours,
} from './register-company.helpers';

type Hours = Parameters<typeof summarizeBusinessHours>[0];

const day = (open: boolean, start: string, end: string) => ({ open, start, end });

function hoursOf(overrides: Partial<Record<Weekday, ReturnType<typeof day>>> = {}): Hours {
  const base: Hours = {
    monday: day(true, '08:00', '17:00'),
    tuesday: day(true, '08:00', '17:00'),
    wednesday: day(true, '08:00', '17:00'),
    thursday: day(true, '08:00', '17:00'),
    friday: day(true, '08:00', '17:00'),
    saturday: day(true, '09:00', '13:00'),
    sunday: day(false, '09:00', '17:00'),
  };
  return { ...base, ...overrides };
}

describe('register-company wizard helpers', () => {
  it('formats time options, copies Monday, summarizes hours, examples and password states (AC-06 to AC-10)', () => {
    // BR-04: 48 half-hour options in 12-hour form, submitted as HH:mm.
    expect(TIME_OPTIONS).toHaveLength(48);
    expect(TIME_OPTIONS[0]).toEqual({ code: '00:00', label: '12:00 AM' });
    expect(TIME_OPTIONS.find((option) => option.code === '08:30')?.label).toBe('8:30 AM');
    expect(TIME_OPTIONS.find((option) => option.code === '12:30')?.label).toBe('12:30 PM');
    expect(TIME_OPTIONS[47]).toEqual({ code: '23:30', label: '11:30 PM' });

    // AC-07: Monday's state goes to Tue-Fri; the weekend is unchanged.
    const openMonday = copyMondayToWeekdays(
      hoursOf({
        monday: day(true, '07:30', '16:00'),
        tuesday: day(false, '08:00', '17:00'),
        saturday: day(true, '10:00', '14:00'),
      }),
    );
    for (const weekday of ['tuesday', 'wednesday', 'thursday', 'friday'] as const) {
      expect(openMonday[weekday]).toEqual(day(true, '07:30', '16:00'));
    }
    expect(openMonday.saturday).toEqual(day(true, '10:00', '14:00'));
    expect(openMonday.sunday.open).toBe(false);
    const closedMonday = copyMondayToWeekdays(hoursOf({ monday: day(false, '08:00', '17:00') }));
    expect(WEEKDAYS.slice(0, 5).every((weekday) => !closedMonday[weekday].open)).toBe(true);
    expect(closedMonday.saturday.open).toBe(true);

    // BR-05 / AC-10.
    const allClosed = Object.fromEntries(WEEKDAYS.map((w) => [w, day(false, '08:00', '17:00')]));
    const everyDay = Object.fromEntries(WEEKDAYS.map((w) => [w, day(true, '08:00', '17:00')]));
    const summaries: [Hours, string][] = [
      [hoursOf(), 'Mon – Fri, 8:00 AM – 5:00 PM · Sat, 9:00 AM – 1:00 PM (Central Time)'],
      [everyDay as Hours, 'Mon – Sun, 8:00 AM – 5:00 PM (Central Time)'],
      [
        {
          ...(allClosed as Hours),
          monday: day(true, '09:00', '13:00'),
          wednesday: day(true, '09:00', '13:00'),
        },
        'Mon, 9:00 AM – 1:00 PM · Wed, 9:00 AM – 1:00 PM (Central Time)',
      ],
      [allClosed as Hours, 'Closed all week'],
    ];
    for (const [hours, expected] of summaries) {
      expect(summarizeBusinessHours(hours, 'America/Chicago')).toBe(expected);
    }

    // BR-06 / AC-08.
    const organization = {
      quotePrefix: ' q ',
      workOrderPrefix: 'WO',
      invoicePrefix: '',
      nextInvoiceNumber: 1049 as number | null,
    };
    expect(numberingExamples(organization)).toBe('Examples: Q-1 · WO-1 · —-1049');
    expect(
      numberingExamples({ ...organization, invoicePrefix: 'inv', nextInvoiceNumber: null }),
    ).toBe('Examples: Q-1 · WO-1 · INV-—');
    expect(numberingExamples({ ...organization, nextInvoiceNumber: 0 })).toContain('—-—');

    // BR-07 / AC-09.
    const email = 'owner@acme.io';
    const states: [string, boolean, boolean][] = [
      ['', false, false],
      ['12345678901', false, true],
      ['OWNER@ACME.IO', true, false],
      ['a different one', true, true],
    ];
    for (const [password, lengthMet, differsFromEmailMet] of states) {
      expect(passwordRequirements(password, email)).toEqual({ lengthMet, differsFromEmailMet });
    }
  });
});
