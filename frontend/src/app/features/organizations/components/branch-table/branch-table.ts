import { Component, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';

import { BranchListItem } from '../../models/company-settings.model';

/** A branch prepared for display (FR-13). */
export interface BranchRow {
  readonly branch: BranchListItem;
  readonly address: string;
  readonly timezoneName: string;
  readonly team: string;
}

export interface BranchMenuRequest {
  readonly event: Event;
  readonly branch: BranchListItem;
}

/**
 * Branch table (cards below 768px): Branch with the Main branch tag, Address, Time zone, Team,
 * Status and a row-menu button; the selected row is highlighted while the drawer is open.
 */
@Component({
  selector: 'app-branch-table',
  imports: [Tag],
  templateUrl: './branch-table.html',
  styleUrl: './branch-table.scss',
})
export class BranchTable {
  readonly rows = input.required<readonly BranchRow[]>();
  readonly sortDirection = input.required<'ascending' | 'descending'>();
  readonly selectedId = input<string | null>(null);

  readonly sortToggled = output<void>();
  readonly rowActivated = output<BranchListItem>();
  readonly menuRequested = output<BranchMenuRequest>();

  onRowKeydown(event: KeyboardEvent, branch: BranchListItem): void {
    if ((event.key === 'Enter' || event.key === ' ') && event.target === event.currentTarget) {
      event.preventDefault();
      this.rowActivated.emit(branch);
    }
  }
}
