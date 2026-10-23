import { Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { TranslocoPipe } from '@jsverse/transloco';

import { t } from '../../../../core/i18n/translate';
import { TechnicianProfileHeader } from '../../components/technician-profile-header/technician-profile-header';
import {
  STATUS_LABELS,
  groupAvailability,
  nextAvailableText,
  statusSeverity,
  workloadView,
} from '../../utils/team-format';
import {
  VISIT_STATUS_LABELS,
  formatLocalDate,
  skillViews,
  timeOffTimes,
} from '../../utils/technician-profile-format';
import { injectProfileLoader } from '../../utils/technician-profile-loader';

export const OVERVIEW_FORBIDDEN_MESSAGE = (): string =>
  t('team.skillsAvailability.forbiddenMessage');

/**
 * Technician Overview (`/team/:technicianId/overview`): read-only profile summary for the Team
 * roles. Backend authorization is authoritative; roles without `TeamView` send no request.
 */
@Component({
  selector: 'app-technician-overview',
  imports: [TranslocoPipe, RouterLink, ButtonDirective, Message, Skeleton, TechnicianProfileHeader],
  templateUrl: './technician-overview.html',
  styleUrl: './technician-overview.scss',
})
export class TechnicianOverview {
  protected readonly profile = injectProfileLoader();
  protected readonly detail = this.profile.detail;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statusSeverity = statusSeverity;
  protected readonly visitStatusLabels = VISIT_STATUS_LABELS;
  protected readonly schedulePath = '/schedule';
  protected readonly skeletons = [0, 1, 2];

  protected readonly skillsPath = computed(
    () => `/team/${this.profile.technicianId()}/skills-availability`,
  );
  /** BR-06: inactive and suspended profiles show "—" and "None". */
  protected readonly active = computed(() => this.detail()?.profileStatus === 'active');
  protected readonly workload = computed(() => {
    const today = this.detail()?.today;
    return today
      ? workloadView(today.workloadState, today.workloadPercent)
      : workloadView('none', null);
  });
  protected readonly summary = computed(() => {
    const detail = this.detail();
    if (!detail) {
      return [];
    }
    const { today } = detail;
    const active = this.active();
    return [
      {
        key: 'currentJob',
        icon: 'pi-briefcase',
        label: t('team.technicianProfile.currentJob'),
        value: (active ? today.currentJob?.label : null) || t('team.technicianProfile.none'),
      },
      {
        key: 'jobsToday',
        icon: 'pi-file',
        label: t('team.technicianProfile.jobsToday'),
        value: active ? String(today.jobs) : '—',
      },
      {
        key: 'workload',
        icon: 'pi-chart-bar',
        label: t('team.technicianProfile.workload'),
        value: active ? this.workload().text : '—',
      },
      {
        key: 'nextAvailable',
        icon: 'pi-calendar',
        label: t('team.technicianProfile.nextAvailable'),
        value: active ? nextAvailableText(today.nextAvailable, detail.timezone) : '—',
      },
    ];
  });
  protected readonly skills = computed(() => {
    const detail = this.detail();
    return detail ? skillViews(detail) : [];
  });
  protected readonly availability = computed(() =>
    groupAvailability(this.detail()?.availability ?? []),
  );
  protected readonly timeOff = computed(() => {
    const off = this.detail()?.upcomingTimeOff;
    return off
      ? {
          date: formatLocalDate(off.date, true),
          times: timeOffTimes(off.allDay, off.start, off.end),
          reason: off.reason,
        }
      : null;
  });
  protected readonly showCurrentJob = computed(() => {
    const detail = this.detail();
    return detail?.today.todayStatus === 'on_job' && detail.today.currentJob != null;
  });

  constructor() {
    this.profile.load();
  }

  protected get forbiddenMessage(): string {
    return OVERVIEW_FORBIDDEN_MESSAGE();
  }
}
