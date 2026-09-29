import { Component, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';
import { TooltipModule } from 'primeng/tooltip';

import { UserSummary } from '../../models/users.model';

interface Metric {
  readonly key: keyof UserSummary;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'primary' | 'warning' | 'danger' | 'info';
}

const METRICS: readonly Metric[] = [
  { key: 'activeUsers', label: 'Active users', icon: 'pi-users', tone: 'primary' },
  { key: 'pendingInvitations', label: 'Pending invitations', icon: 'pi-envelope', tone: 'warning' },
  { key: 'suspendedUsers', label: 'Suspended', icon: 'pi-ban', tone: 'danger' },
  { key: 'owners', label: 'Owners', icon: 'pi-shield', tone: 'info' },
];

export const METRICS_ERROR_TOOLTIP = "Couldn't load";

/** Four metric cards: icon + label + number; skeletons while loading, "—" with a tooltip on failure (BR-14). */
@Component({
  selector: 'app-user-metrics',
  imports: [Skeleton, TooltipModule],
  templateUrl: './user-metrics.html',
  styleUrl: './user-metrics.scss',
})
export class UserMetrics {
  readonly summary = input<UserSummary | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly metrics = METRICS;
  readonly errorTooltip = METRICS_ERROR_TOOLTIP;
}
