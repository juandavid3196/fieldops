import { Component, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';
import { TooltipModule } from 'primeng/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { UserSummary } from '../../models/users.model';

import { localizedList, t } from '../../../../core/i18n/translate';
interface Metric {
  readonly key: keyof UserSummary;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'primary' | 'warning' | 'danger' | 'info';
}

const METRICS: readonly Metric[] = localizedList(() => [
  {
    key: 'activeUsers',
    label: t('users.userMetrics.activeUsers'),
    icon: 'pi-users',
    tone: 'primary',
  },
  {
    key: 'pendingInvitations',
    label: t('users.userMetrics.pendingInvitations'),
    icon: 'pi-envelope',
    tone: 'warning',
  },
  {
    key: 'suspendedUsers',
    label: t('users.userMetrics.suspended'),
    icon: 'pi-ban',
    tone: 'danger',
  },
  { key: 'owners', label: t('users.userMetrics.owners'), icon: 'pi-shield', tone: 'info' },
]);

export const METRICS_ERROR_TOOLTIP = (): string => t('users.userMetrics.metricsErrorTooltip');

/** Four metric cards: icon + label + number; skeletons while loading, "—" with a tooltip on failure (BR-14). */
@Component({
  selector: 'app-user-metrics',
  imports: [TranslocoPipe, Skeleton, TooltipModule],
  templateUrl: './user-metrics.html',
  styleUrl: './user-metrics.scss',
})
export class UserMetrics {
  readonly summary = input<UserSummary | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly metrics = METRICS;
  get errorTooltip(): string {
    return METRICS_ERROR_TOOLTIP();
  }
}
