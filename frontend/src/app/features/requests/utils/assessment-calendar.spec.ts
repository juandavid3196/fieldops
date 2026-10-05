import { AssessmentCalendar, CalendarDay } from '../models/requests.model';
import {
  CalendarViewInput,
  buildCalendarView,
  rangeLabel,
  shiftDate,
  weekStart,
} from './assessment-calendar';

const ZONE = 'America/Chicago';
const VIEW_DATE = '2026-09-22'; // Tuesday; the week starts Monday 2026-09-21.

const day = (date: string, extra: Partial<CalendarDay> = {}): CalendarDay => ({
  date,
  availability: [{ start: '08:00', end: '17:00' }],
  timeOff: [],
  breaks: [],
  ...extra,
});

// CDT is UTC-5: 14:00Z is 9:00 AM local.
const CALENDAR: AssessmentCalendar = {
  timezone: ZONE,
  days: [
    day('2026-09-21', { breaks: [{ start: '12:00', end: '13:00' }] }),
    day('2026-09-22'),
    day('2026-09-23', { timeOff: [{ start: '13:00', end: '17:00' }] }),
    day('2026-09-24'),
    day('2026-09-25'),
    day('2026-09-26', { availability: [] }),
    day('2026-09-27', { availability: [] }),
  ],
  events: [
    {
      kind: 'assessment',
      start: '2026-09-21T14:00:00Z',
      end: '2026-09-21T15:00:00Z',
      label: 'Water heater estimate',
      isCurrent: false,
    },
    {
      kind: 'assessment',
      start: '2026-09-22T15:00:00Z',
      end: '2026-09-22T16:00:00Z',
      label: 'Kitchen sink leak',
      isCurrent: true,
    },
    {
      kind: 'visit',
      start: '2026-09-26T15:00:00Z',
      end: '2026-09-26T17:00:00Z',
      label: 'Visit',
      isCurrent: false,
    },
  ],
};

const view = (input: Partial<CalendarViewInput> = {}) =>
  buildCalendarView({
    calendar: CALENDAR,
    view: 'week',
    viewDate: VIEW_DATE,
    proposed: { date: VIEW_DATE, startMinutes: 600, durationMinutes: 60 },
    timeZone: ZONE,
    ...input,
  });

describe('assessment calendar view model (BR-07)', () => {
  it('builds Week, Day and Agenda views with weekend columns, hour range, shading, current and proposed blocks', () => {
    const week = view();
    // Saturday has a commitment, Sunday has nothing: Monday to Saturday.
    expect(week.columns.map((column) => column.date)).toEqual([
      '2026-09-21',
      '2026-09-22',
      '2026-09-23',
      '2026-09-24',
      '2026-09-25',
      '2026-09-26',
    ]);
    expect(week.columns[0].weekday).toBe('Mon');
    expect(week.columns[0].dayLabel).toBe('Sep 21');
    expect(week.hourCount).toBe(9); // 8:00 AM to 5:00 PM from availability and commitments
    expect(week.hours[0]).toEqual({ label: '8 AM', top: 0 });

    const monday = week.columns[0];
    expect(monday.events[0]).toMatchObject({
      label: 'Water heater estimate',
      range: '9:00 – 10:00 AM',
    });
    // The 12:00-1:00 PM break is shaded as unavailable: (12 - 8) / 9 of the grid height.
    expect(monday.shades).toContainEqual({
      kind: 'unavailable',
      top: expect.closeTo(44.44, 1),
      height: expect.closeTo(11.11, 1),
    });
    // Time off is its own shade.
    expect(week.columns[2].shades.some((shade) => shade.kind === 'time-off')).toBe(true);

    // Tuesday: the current assessment and the proposed block overlap, so they sit side by side.
    const tuesday = week.columns[1];
    expect(tuesday.selected).toBe(true);
    expect(tuesday.events.map((event) => [event.label, event.kind, event.lanes])).toEqual([
      ['Current assessment', 'assessment', 2],
      ['Proposed assessment', 'proposed', 2],
    ]);
    expect(week.columns[5].events[0].label).toBe('Visit');

    expect(view({ view: 'day' }).columns.map((column) => column.date)).toEqual([VIEW_DATE]);

    const agenda = view({ view: 'agenda' });
    expect(agenda.agenda.map((entry) => entry.date)).toEqual([
      '2026-09-21',
      '2026-09-22',
      '2026-09-26',
    ]);
    expect(agenda.agenda[1].events).toEqual([
      { range: '10:00 – 11:00 AM', label: 'Current assessment' },
    ]);
    expect(week.listing[0]).toBe(
      'Monday, September 21, 9:00 AM to 10:00 AM: Water heater estimate',
    );

    // Nothing to show: Monday to Friday, 8:00 AM to 6:00 PM, availability shading only.
    const empty = view({ calendar: { timezone: ZONE, days: [], events: [] }, proposed: null });
    expect(empty.columns).toHaveLength(5);
    expect(empty.hourCount).toBe(10);
    expect(empty.agenda).toEqual([]);
    expect(empty.columns.every((column) => column.events.length === 0)).toBe(true);
  });

  it('navigates by week or day and labels the range', () => {
    expect(weekStart('2026-09-27')).toBe('2026-09-21'); // Sunday belongs to the previous Monday
    expect(shiftDate('week', VIEW_DATE, 1)).toBe('2026-09-29');
    expect(shiftDate('agenda', VIEW_DATE, -1)).toBe('2026-09-15');
    expect(shiftDate('day', VIEW_DATE, 1)).toBe('2026-09-23');
    expect(rangeLabel('week', VIEW_DATE)).toBe('Sep 21 – 25, 2026');
    expect(rangeLabel('week', '2026-09-30')).toBe('Sep 28 – Oct 2, 2026');
    expect(rangeLabel('day', VIEW_DATE)).toBe('Tue, Sep 22, 2026');
  });
});
