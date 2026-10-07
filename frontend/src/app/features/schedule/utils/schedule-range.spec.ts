import { CalendarTechnician, CalendarVisit } from '../models/schedule.model';
import {
  blockGeometry,
  formatPreferred,
  hourAxis,
  loadInfo,
  rangeDays,
  rangeLabel,
  shiftDate,
  todayInZone,
  visibleDays,
} from './schedule-range';

const TZ = 'America/Chicago';

function technician(overrides: Partial<CalendarTechnician> = {}): CalendarTechnician {
  return {
    id: 't-1',
    name: 'Carlos Rivera',
    initials: 'CR',
    colorHex: null,
    primarySkill: null,
    tag: null,
    load: { scheduledMinutes: 0, availableMinutes: 480, state: 'percent' },
    days: [],
    assessments: [],
    ...overrides,
  };
}

function visit(start: string): CalendarVisit {
  return {
    visitId: 'v-1',
    workOrderId: 'w-1',
    displayNumber: 'WO-1',
    title: 'Repair',
    street: '1 Main St',
    status: 'scheduled',
    start,
    end: start,
    technicianIds: [],
    primaryTechnicianId: null,
    conflicts: [],
  };
}

describe('schedule range helpers (BR-04, BR-05, AC-02, AC-03)', () => {
  it('builds Monday-Sunday weeks, labels and navigation steps', () => {
    // 2026-09-23 is a Wednesday.
    expect(rangeDays('week', '2026-09-23')[0]).toBe('2026-09-21');
    expect(rangeDays('week', '2026-09-23')).toHaveLength(7);
    expect(rangeDays('day', '2026-09-23')).toEqual(['2026-09-23']);
    expect(rangeLabel('week', '2026-09-23')).toBe('Sep 21 – 27, 2026');
    expect(rangeLabel('week', '2026-09-30')).toBe('Sep 28 – Oct 4, 2026');
    expect(rangeLabel('day', '2026-09-23')).toBe('Wed, Sep 23, 2026');
    expect(shiftDate('week', '2026-09-23', 1)).toBe('2026-09-30');
    expect(shiftDate('day', '2026-09-23', -1)).toBe('2026-09-22');
  });

  it('collapses empty weekends and derives the Day hour axis from availability', () => {
    const week = rangeDays('week', '2026-09-23');
    const technicians = [technician()];
    expect(visibleDays(week, technicians, [], TZ)).toHaveLength(5);

    // A Saturday visit (10:00 local = 15:00Z) keeps the Saturday column.
    const withSaturday = visibleDays(week, technicians, [visit('2026-09-26T15:00:00Z')], TZ);
    expect(withSaturday).toContain('2026-09-26');
    expect(withSaturday).not.toContain('2026-09-27');

    // Default axis, then availability 7:30-18:15 local rounds out to whole hours.
    expect(hourAxis([], '2026-09-23', TZ)).toMatchObject({ startHour: 8, endHour: 17 });
    const available = technician({
      days: [
        {
          date: '2026-09-23',
          availability: [{ start: '2026-09-23T12:30:00Z', end: '2026-09-23T23:15:00Z' }],
          breaks: [],
          timeOff: [],
        },
      ],
    });
    const axis = hourAxis([available], '2026-09-23', TZ);
    expect(axis).toMatchObject({ startHour: 7, endHour: 19 });
    expect(blockGeometry(8 * 60, 9 * 60, axis).left).toBeCloseTo((1 / 12) * 100);
  });

  it.each([
    [
      { scheduledMinutes: 180, availableMinutes: 480, state: 'percent' as const },
      '3h / 8h',
      'teal',
    ],
    [
      { scheduledMinutes: 300, availableMinutes: 480, state: 'percent' as const },
      '5h / 8h',
      'amber',
    ],
    [{ scheduledMinutes: 540, availableMinutes: 480, state: 'percent' as const }, '9h / 8h', 'red'],
    [
      { scheduledMinutes: 60, availableMinutes: 0, state: 'no_availability' as const },
      'No availability',
      'red',
    ],
  ])('shows load %o as "%s" with a %s bar', (load, text, tone) => {
    expect(loadInfo(load)).toMatchObject({ text, tone });
  });

  it('formats instants in the branch time zone', () => {
    expect(todayInZone(TZ, new Date('2026-10-07T03:30:00Z'))).toBe('2026-10-06');
    expect(formatPreferred('2026-09-23T14:00:00Z', '2026-09-23T17:00:00Z', TZ)).toBe(
      'Wed, Sep 23, 9:00 AM – 12:00 PM',
    );
    expect(formatPreferred(null, null, TZ)).toBe('No preferred date');
  });
});
