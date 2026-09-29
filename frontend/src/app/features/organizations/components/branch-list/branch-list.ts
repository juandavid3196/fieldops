import {
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { timezoneGenericName } from '../../data/display-names';
import { BranchListItem } from '../../models/company-settings.model';
import { BranchesService } from '../../services/branches.service';
import { BranchRow, BranchTable } from '../branch-table/branch-table';

export const ONLY_ACTIVE_BRANCH_TOOLTIP = 'At least one branch must stay active.';
export const MAIN_BRANCH_TOOLTIP = "The main branch can't be deactivated.";
export const INACTIVE_MAIN_MESSAGE = 'Only an active branch can be the main branch.';
export const BRANCH_LOAD_ERROR_MESSAGE =
  "We couldn't load branches. Check your connection and try again.";

type StatusFilter = 'all' | 'active' | 'inactive';
type SortDirection = 'ascending' | 'descending';

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const STATUS_OPTIONS: { code: StatusFilter; label: string }[] = [
  { code: 'all', label: 'All statuses' },
  { code: 'active', label: 'Active' },
  { code: 'inactive', label: 'Inactive' },
];

export function teamText(count: number): string {
  return count === 1 ? '1 technician' : `${count} technicians`;
}

function addressText(branch: BranchListItem): string {
  const region = [branch.stateRegion, branch.postalCode].filter((part) => part !== '').join(' ');
  return [branch.addressLine1, branch.city, region].filter((part) => part !== '').join(', ');
}

/**
 * Branch list (FR-13, FR-15): client-side search, status filter and Branch-column sort over
 * `GET /branches`, the Main branch tag, Team and generic time zone, the selected-row
 * highlight and a row menu (Edit/View, Set as main branch, Deactivate, Reactivate).
 */
@Component({
  selector: 'app-branch-list',
  imports: [
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Select,
    Skeleton,
    BranchTable,
  ],
  templateUrl: './branch-list.html',
  styleUrl: './branch-list.scss',
})
export class BranchList {
  private readonly branchesService = inject(BranchesService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly menu = viewChild.required(Menu);

  readonly branches = input.required<readonly BranchListItem[]>();
  readonly loading = input(false);
  readonly loadError = input<ApiError | null>(null);
  readonly canManage = input.required<boolean>();
  /** The branch open in the drawer (highlighted row). */
  readonly selectedId = input<string | null>(null);

  readonly rowActivated = output<string>();
  readonly addBranch = output<void>();
  readonly changed = output<void>();
  readonly retry = output<void>();
  readonly unauthorized = output<void>();

  readonly statusOptions = STATUS_OPTIONS;
  readonly loadErrorMessage = BRANCH_LOAD_ERROR_MESSAGE;

  readonly search = signal('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly sortDirection = signal<SortDirection>('ascending');
  readonly mutatingId = signal<string | null>(null);
  readonly menuModel = signal<MenuItem[]>([]);

  readonly onlyActiveBranchId = computed(() => {
    const active = this.branches().filter((branch) => branch.isActive);
    return active.length === 1 ? active[0].id : null;
  });

  readonly filtered = computed<readonly BranchRow[]>(() => {
    const term = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    const direction = this.sortDirection();

    const matches = this.branches().filter((branch) => {
      if (status === 'active' && !branch.isActive) {
        return false;
      }
      if (status === 'inactive' && branch.isActive) {
        return false;
      }
      if (term.length === 0) {
        return true;
      }
      return (
        branch.name.toLowerCase().includes(term) ||
        branch.code.toLowerCase().includes(term) ||
        branch.city.toLowerCase().includes(term)
      );
    });

    const sorted = [...matches].sort((a, b) => a.name.localeCompare(b.name));
    const ordered = direction === 'ascending' ? sorted : sorted.reverse();
    return ordered.map((branch) => ({
      branch,
      address: addressText(branch),
      timezoneName: timezoneGenericName(branch.timezone),
      team: teamText(branch.technicianCount),
    }));
  });

  readonly hasFilters = computed(
    () => this.search().trim().length > 0 || this.statusFilter() !== 'all',
  );

  toggleSort(): void {
    this.sortDirection.set(this.sortDirection() === 'ascending' ? 'descending' : 'ascending');
  }

  clearFilters(): void {
    this.search.set('');
    this.statusFilter.set('all');
  }

  isOnlyActive(branch: BranchListItem): boolean {
    return branch.isActive && this.onlyActiveBranchId() === branch.id;
  }

  onRowActivate(branch: BranchListItem): void {
    this.rowActivated.emit(branch.id);
  }

  /** Opens the row menu for one branch (FR-15): items depend on role, activity and main flag. */
  openMenu(event: Event, branch: BranchListItem): void {
    event.stopPropagation();
    this.menuModel.set(this.buildMenu(branch));
    this.menu().toggle(event);
  }

  buildMenu(branch: BranchListItem): MenuItem[] {
    const busy = this.mutatingId() !== null;
    const items: MenuItem[] = [
      {
        label: this.canManage() ? 'Edit' : 'View',
        icon: this.canManage() ? 'pi pi-pencil' : 'pi pi-eye',
        command: () => this.onRowActivate(branch),
      },
    ];
    if (this.canManage()) {
      if (branch.isActive && !branch.isMain) {
        items.push({
          label: 'Set as main branch',
          icon: 'pi pi-star',
          disabled: busy,
          command: () => this.setMain(branch),
        });
      }
      if (branch.isActive) {
        const tooltip = branch.isMain
          ? MAIN_BRANCH_TOOLTIP
          : this.isOnlyActive(branch)
            ? ONLY_ACTIVE_BRANCH_TOOLTIP
            : undefined;
        items.push({
          label: 'Deactivate',
          icon: 'pi pi-ban',
          disabled: busy || tooltip !== undefined,
          tooltip,
          title: tooltip,
          tooltipPosition: 'left',
          command: () => this.deactivate(branch),
        });
      } else {
        items.push({
          label: 'Reactivate',
          icon: 'pi pi-refresh',
          disabled: busy,
          command: () => this.reactivate(branch),
        });
      }
    }
    return items;
  }

  deactivate(branch: BranchListItem): void {
    if (this.mutatingId() !== null || branch.isMain || this.isOnlyActive(branch)) {
      return;
    }
    this.confirmationService.confirm({
      header: 'Deactivate branch',
      message: `Deactivate ${branch.name}? No new requests, quotes or work orders can be created for this branch. Existing records stay available.`,
      acceptButtonProps: { label: 'Deactivate', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.performStateChange(branch, 'deactivate'),
    });
  }

  reactivate(branch: BranchListItem): void {
    if (this.mutatingId() !== null) {
      return;
    }
    this.performStateChange(branch, 'reactivate');
  }

  setMain(branch: BranchListItem): void {
    if (this.mutatingId() !== null || !branch.isActive || branch.isMain) {
      return;
    }
    this.mutatingId.set(branch.id);
    this.branchesService
      .setMain(branch.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.mutatingId.set(null);
          this.messageService.add({
            severity: 'success',
            summary: `${branch.name} is now the main branch`,
          });
          this.changed.emit();
        },
        error: (error: unknown) => this.handleMutationFailed(error, INACTIVE_MAIN_MESSAGE),
      });
  }

  private performStateChange(branch: BranchListItem, action: 'deactivate' | 'reactivate'): void {
    this.mutatingId.set(branch.id);
    const request$ =
      action === 'deactivate'
        ? this.branchesService.deactivate(branch.id)
        : this.branchesService.reactivate(branch.id);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.mutatingId.set(null);
        const verb = action === 'deactivate' ? 'deactivated' : 'reactivated';
        this.messageService.add({ severity: 'success', summary: `${branch.name} ${verb}` });
        this.changed.emit();
      },
      error: (error: unknown) =>
        this.handleMutationFailed(
          error,
          branch.isMain ? MAIN_BRANCH_TOOLTIP : ONLY_ACTIVE_BRANCH_TOOLTIP,
        ),
    });
  }

  private handleMutationFailed(error: unknown, conflict: string): void {
    this.mutatingId.set(null);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.messageService.add({
        severity: 'error',
        summary: 'This branch is no longer available.',
      });
      this.changed.emit();
      return;
    }
    if (apiError?.kind === 'conflict') {
      this.messageService.add({ severity: 'error', summary: conflict });
      return;
    }
    this.messageService.add({
      severity: 'error',
      summary: apiError?.message ?? 'An unexpected error occurred. Please try again.',
    });
  }
}
