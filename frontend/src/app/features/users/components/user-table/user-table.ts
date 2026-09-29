import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Avatar } from 'primeng/avatar';
import { ButtonDirective } from 'primeng/button';
import { Tag } from 'primeng/tag';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { UserRow, UserSort } from '../../models/users.model';
import { branchAccessText } from '../../utils/user-access';
import {
  DisplayStatus,
  STATUS_LABELS,
  displayStatus,
  fullName,
  initials,
  lastActiveText,
  teamProfileText,
} from '../../utils/user-format';

const STATUS_PRESENTATION: Readonly<
  Record<DisplayStatus, { icon: string; severity: 'success' | 'danger' | 'warn' }>
> = {
  active: { icon: 'pi pi-check-circle', severity: 'success' },
  suspended: { icon: 'pi pi-ban', severity: 'danger' },
  pending: { icon: 'pi pi-clock', severity: 'warn' },
};

/**
 * Users table (FR-04, FR-09): seven columns from md, cards below. Presentational: the page
 * owns data, paging, sorting and the row menu.
 */
@Component({
  selector: 'app-user-table',
  imports: [RouterLink, Avatar, ButtonDirective, Tag],
  templateUrl: './user-table.html',
  styleUrl: './user-table.scss',
})
export class UserTable {
  readonly rows = input.required<readonly UserRow[]>();
  readonly sort = input<UserSort>('name');
  readonly canManage = input.required<boolean>();
  /** Row whose mutation is in flight (its actions are locked). */
  readonly busyId = input<string | null>(null);

  readonly sortToggled = output<void>();
  readonly editRequested = output<UserRow>();
  readonly menuRequested = output<{ event: Event; row: UserRow }>();

  readonly teamPath = comingSoonPath('team');
  readonly statusLabels = STATUS_LABELS;
  readonly statusPresentation = STATUS_PRESENTATION;

  readonly fullName = fullName;
  readonly initials = initials;
  readonly displayStatus = displayStatus;
  readonly branchAccessText = branchAccessText;
  readonly teamProfileText = teamProfileText;
  readonly lastActive = (row: UserRow): string => lastActiveText(row.lastActiveAt);
}
