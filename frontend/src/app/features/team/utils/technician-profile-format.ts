import { JobHistoryOutcome } from '../models/team-job-history.model';
import { TechnicianDetail, VisitStatus } from '../models/team.model';
import { formatWindowTime, proficiencyLabel } from './team-format';

import { activeIntlLocale } from '../../../core/i18n/active-locale';
import { localized, t } from '../../../core/i18n/translate';

export const VISIT_STATUS_LABELS: Readonly<Record<VisitStatus, string>> = localized(() => ({
  on_the_way: t('team.technicianOverview.onTheWay'),
  in_progress: t('team.technicianOverview.inProgress'),
  paused: t('team.technicianOverview.paused'),
}));

export const OUTCOME_LABELS: Readonly<Record<JobHistoryOutcome, string>> = localized(() => ({
  completed: t('team.technicianJobHistory.completed'),
  reassigned: t('team.technicianJobHistory.reassigned'),
  cancelled: t('team.technicianJobHistory.cancelled'),
}));

export function outcomeSeverity(outcome: JobHistoryOutcome): 'success' | 'warn' | 'secondary' {
  return outcome === 'completed' ? 'success' : outcome === 'reassigned' ? 'warn' : 'secondary';
}

export function initialsOf(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase();
}

/** Local `yyyy-MM-dd` as "MMM d, yyyy" or "EEE, MMM d, yyyy" in the active language. */
export function formatLocalDate(date: string, withWeekday = false): string {
  const parsed = new Date(`${date}T12:00:00Z`);
  if (Number.isNaN(parsed.getTime())) {
    return '—';
  }
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    ...(withWeekday ? { weekday: 'short' as const } : {}),
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(parsed);
}

/** `technician-time-summary` BR-07: "<h>h <m>m", "<m>m" under one hour, "0m" for zero. */
export function formatMinutes(minutes: number): string {
  const total = Math.max(0, Math.round(minutes));
  const hours = Math.floor(total / 60);
  return hours === 0 ? `${total}m` : `${hours}h ${total % 60}m`;
}

/** Optional metric: "—" when absent. */
export function optionalMinutes(minutes: number | null | undefined): string {
  return typeof minutes === 'number' ? formatMinutes(minutes) : '—';
}

export function timeOffTimes(allDay: boolean, start?: string | null, end?: string | null): string {
  return allDay || !start || !end
    ? t('team.technicianOverview.allDay')
    : `${formatWindowTime(start)} – ${formatWindowTime(end)}`;
}

export type SkillTone = 'success' | 'info' | 'warn';

export interface SkillView {
  readonly name: string;
  readonly text: string;
  readonly level: string | null;
  readonly icon: string;
  readonly tone: SkillTone;
  readonly primary: boolean;
}

const SKILL_TONES: readonly SkillTone[] = ['success', 'info', 'warn'];

/** Decorative icon by skill name keyword; any other skill gets a wrench. */
function skillIcon(name: string): string {
  const key = name.toLowerCase();
  if (/electric|elèctr|eléctr/.test(key)) {
    return 'pi-bolt';
  }
  if (/hvac|air|climat|cool|heat/.test(key)) {
    return 'pi-sparkles';
  }
  return 'pi-wrench';
}

/** BR-09: primary first, then name A–Z, "<name> · <level>". */
export function skillViews(detail: TechnicianDetail): readonly SkillView[] {
  return [...detail.skills]
    .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.name.localeCompare(b.name))
    .map((skill, index) => {
      const level = proficiencyLabel(skill.proficiency);
      return {
        name: skill.name,
        text: level === null ? skill.name : `${skill.name} · ${level}`,
        level,
        icon: skillIcon(skill.name),
        tone: SKILL_TONES[index % SKILL_TONES.length],
        primary: skill.isPrimary,
      };
    });
}
