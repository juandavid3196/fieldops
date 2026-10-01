import {
  AssignedSkillDto,
  CatalogSkill,
  DayForm,
  ExceptionKind,
  SkillAssignmentRequest,
  SkillRow,
  UpcomingDay,
  WeeklyDayDto,
  WeeklyDayRequest,
} from '../models/skills-availability.model';
import { formatWindowTime } from './team-format';

export const DAY_NAMES = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];
/** BR-03: Monday first, Sunday last. */
export const WEEK_ORDER = [1, 2, 3, 4, 5, 6, 0];
export const DEFAULT_START = '08:00';
export const DEFAULT_END = '17:00';
export const DEFAULT_LEVEL = 3;

export const LEVEL_OPTIONS: { readonly value: number; readonly label: string }[] = [
  { value: 1, label: 'Beginner' },
  { value: 2, label: 'Basic' },
  { value: 3, label: 'Intermediate' },
  { value: 4, label: 'Advanced' },
  { value: 5, label: 'Expert' },
];

export interface Option<T> {
  readonly value: T;
  readonly label: string;
}

export function toMinutes(value: string): number {
  const [hours, minutes] = value.split(':').map(Number);
  return hours * 60 + minutes;
}

export function toHm(minutes: number): string {
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

/** BR-05: 30-minute steps from 12:00 AM to 11:30 PM. */
export const TIME_OPTIONS: Option<string>[] = Array.from({ length: 48 }, (_, index) => {
  const value = toHm(index * 30);
  return { value, label: formatWindowTime(value) };
});

const hm = (value: string | null | undefined): string | null =>
  value === null || value === undefined || value === '' ? null : value.slice(0, 5);

// Weekly availability (BR-05–BR-08)

export function toDayForms(weekly: readonly WeeklyDayDto[]): DayForm[] {
  return WEEK_ORDER.map((dayOfWeek) => {
    const saved = weekly.find((day) => day.dayOfWeek === dayOfWeek);
    return saved
      ? {
          dayOfWeek,
          on: true,
          start: hm(saved.start) ?? DEFAULT_START,
          end: hm(saved.end) ?? DEFAULT_END,
          breakStart: hm(saved.breakStart),
          breakEnd: hm(saved.breakEnd),
          capacityPercent: saved.capacityPercent,
        }
      : {
          dayOfWeek,
          on: false,
          start: DEFAULT_START,
          end: DEFAULT_END,
          breakStart: null,
          breakEnd: null,
          capacityPercent: 100,
        };
  });
}

export function breakFits(day: DayForm): boolean {
  if (day.breakStart === null || day.breakEnd === null) {
    return true;
  }
  const start = toMinutes(day.breakStart);
  const end = toMinutes(day.breakEnd);
  return (
    start > toMinutes(day.start) &&
    end < toMinutes(day.end) &&
    (end - start === 30 || end - start === 60)
  );
}

/** BR-06: changing the window so the break no longer fits clears it. */
export function withWindow(day: DayForm, patch: Partial<Pick<DayForm, 'start' | 'end'>>): DayForm {
  const next = { ...day, ...patch };
  return breakFits(next) ? next : { ...next, breakStart: null, breakEnd: null };
}

export function windowValid(day: DayForm): boolean {
  return !day.on || toMinutes(day.start) < toMinutes(day.end);
}

export function dayMinutes(day: DayForm): number {
  if (!day.on || !windowValid(day)) {
    return 0;
  }
  const worked = toMinutes(day.end) - toMinutes(day.start);
  const rest =
    day.breakStart !== null && day.breakEnd !== null
      ? toMinutes(day.breakEnd) - toMinutes(day.breakStart)
      : 0;
  return Math.max(worked - rest, 0);
}

/** BR-07: hours weighted by the stored capacity percent. */
export function dayHours(day: DayForm): number {
  return (dayMinutes(day) / 60) * (day.capacityPercent / 100);
}

/** "8h", "7.5h": up to one decimal. */
export function formatHours(hours: number): string {
  return `${Math.round(hours * 10) / 10}h`;
}

export function weeklyTotalHours(days: readonly DayForm[]): number {
  return Math.round(days.reduce((sum, day) => sum + dayHours(day), 0) * 10) / 10;
}

/** BR-08: Monday's toggle, window and break to Tuesday–Friday; capacity is never copied. */
export function copyMondayToWeekdays(days: readonly DayForm[]): DayForm[] {
  const monday = days.find((day) => day.dayOfWeek === 1);
  if (!monday) {
    return [...days];
  }
  return days.map((day) =>
    day.dayOfWeek >= 2 && day.dayOfWeek <= 5
      ? {
          ...day,
          on: monday.on,
          start: monday.start,
          end: monday.end,
          breakStart: monday.breakStart,
          breakEnd: monday.breakEnd,
        }
      : day,
  );
}

export function breakOptions(day: DayForm): Option<string>[] {
  const options: Option<string>[] = [{ value: '', label: 'No break' }];
  const start = toMinutes(day.start);
  const end = toMinutes(day.end);
  for (let from = start + 30; from < end; from += 30) {
    for (const length of [30, 60]) {
      if (from + length < end) {
        options.push({
          value: `${toHm(from)}|${toHm(from + length)}`,
          label: `${formatWindowTime(toHm(from))} – ${formatWindowTime(toHm(from + length))}`,
        });
      }
    }
  }
  return options;
}

export function breakValue(day: DayForm): string {
  return day.breakStart !== null && day.breakEnd !== null
    ? `${day.breakStart}|${day.breakEnd}`
    : '';
}

export function toWeeklyRequest(days: readonly DayForm[]): WeeklyDayRequest[] {
  return days
    .filter((day) => day.on)
    .map((day) => ({
      dayOfWeek: day.dayOfWeek,
      start: day.start,
      end: day.end,
      ...(day.breakStart !== null && day.breakEnd !== null
        ? { breakStart: day.breakStart, breakEnd: day.breakEnd }
        : {}),
    }))
    .sort((a, b) => a.dayOfWeek - b.dayOfWeek);
}

// Skills (BR-09, BR-10)

const byName = (a: { name: string }, b: { name: string }): number =>
  a.name.localeCompare(b.name, 'en', { sensitivity: 'base' });

export function toSkillRows(skills: readonly AssignedSkillDto[]): SkillRow[] {
  return skills.map(({ skillId, name, proficiency, isPrimary }) => ({
    skillId,
    name,
    proficiency,
    isPrimary,
  }));
}

/** Primary first, then name A–Z. */
export function sortSkills(rows: readonly SkillRow[]): SkillRow[] {
  return [...rows].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || byName(a, b));
}

export function addSkill(
  rows: readonly SkillRow[],
  skill: { readonly id: string; readonly name: string },
): SkillRow[] {
  return [
    ...rows,
    {
      skillId: skill.id,
      name: skill.name,
      proficiency: DEFAULT_LEVEL,
      isPrimary: rows.length === 0,
    },
  ];
}

export function removeSkill(rows: readonly SkillRow[], skillId: string): SkillRow[] {
  const removed = rows.find((row) => row.skillId === skillId);
  const rest = rows.filter((row) => row.skillId !== skillId);
  if (!removed?.isPrimary || rest.length === 0) {
    return rest;
  }
  const promoted = [...rest].sort(byName)[0].skillId;
  return rest.map((row) => ({ ...row, isPrimary: row.skillId === promoted }));
}

/** BR-09: rows of skills now inactive leave the form; a removed primary is promoted. */
export function dropInactive(
  rows: readonly SkillRow[],
  catalog: readonly CatalogSkill[],
): SkillRow[] {
  const inactive = new Set(catalog.filter((skill) => !skill.isActive).map((skill) => skill.id));
  return [...inactive].reduce((current, skillId) => removeSkill(current, skillId), [...rows]);
}

export function setPrimary(rows: readonly SkillRow[], skillId: string): SkillRow[] {
  return rows.map((row) => ({ ...row, isPrimary: row.skillId === skillId }));
}

export function setLevel(rows: readonly SkillRow[], skillId: string, level: number): SkillRow[] {
  return rows.map((row) => (row.skillId === skillId ? { ...row, proficiency: level } : row));
}

/** Picker choices: active organization skills not yet assigned. */
export function pickerSkills(
  catalog: readonly CatalogSkill[],
  rows: readonly SkillRow[],
): CatalogSkill[] {
  const assigned = new Set(rows.map((row) => row.skillId));
  return catalog.filter((skill) => skill.isActive && !assigned.has(skill.id));
}

export function toSkillsRequest(rows: readonly SkillRow[]): SkillAssignmentRequest[] {
  return [...rows]
    .sort((a, b) => a.skillId.localeCompare(b.skillId))
    .map(({ skillId, proficiency, isPrimary }) => ({ skillId, proficiency, isPrimary }));
}

// Field errors (`errors.weeklyAvailability[{dayOfWeek}].<field>`)

export type DayErrors = Readonly<Record<number, Readonly<Record<string, string>>>>;

export function splitWeeklyErrors(fieldErrors: Readonly<Record<string, readonly string[]>>): {
  readonly days: DayErrors;
  readonly rest: Readonly<Record<string, string>>;
} {
  const days: Record<number, Record<string, string>> = {};
  const rest: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const match = /^weeklyAvailability\[(\d)\]\.(\w+)$/i.exec(key);
    if (match) {
      (days[Number(match[1])] ??= {})[match[2].toLowerCase()] = messages[0];
    } else {
      rest[key.charAt(0).toLowerCase() + key.slice(1)] = messages[0];
    }
  }
  return { days, rest };
}

// Display helpers (BR-03, BR-16, BR-18, BR-19)

export const KIND_LABELS: Readonly<Record<ExceptionKind, string>> = {
  extended: 'Extended availability',
  partial: 'Partially unavailable',
  unavailable: 'Unavailable',
};

function localDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
}

/** "MMM d, yyyy" from `yyyy-MM-dd`. */
export function formatExceptionDate(value: string): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(localDate(value));
}

/** "EEE, MMM d" from `yyyy-MM-dd`. */
export function formatUpcomingDate(value: string): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(localDate(value));
}

export function exceptionTime(start?: string | null, end?: string | null): string {
  return start && end
    ? `${formatWindowTime(start.slice(0, 5))} – ${formatWindowTime(end.slice(0, 5))}`
    : 'All day';
}

export function windowsText(windows: UpcomingDay['windows']): string {
  return windows
    .map((window) => `${formatWindowTime(window.start)} – ${formatWindowTime(window.end)}`)
    .join(' and ');
}

/** "yyyy-MM-dd" of today in a time zone, for the exception date minimum. */
export function todayIn(timeZone: string, now: Date = new Date()): string {
  let zone: string | undefined = timeZone;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone });
  } catch {
    zone = undefined;
  }
  return new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: zone,
  }).format(now);
}

/** Hours with up to one decimal from minutes. */
export function minutesToHours(minutes: number): string {
  return formatHours(minutes / 60);
}

export interface UtilizationView {
  readonly text: string;
  readonly fill: number | null;
  readonly remainder: number | null;
  readonly over: boolean;
}

/** BR-19: "—" with no availability; bar capped at 100, red above. */
export function utilizationView(percent: number | null | undefined): UtilizationView {
  if (typeof percent !== 'number') {
    return { text: '—', fill: null, remainder: null, over: false };
  }
  const fill = Math.min(100, Math.max(0, percent));
  return { text: `${percent}%`, fill, remainder: 100 - fill, over: percent > 100 };
}
