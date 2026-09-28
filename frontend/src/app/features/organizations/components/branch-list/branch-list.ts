import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { BranchListItem } from '../../models/company-settings.model';
import { BranchesService } from '../../services/branches.service';

export const ONLY_ACTIVE_BRANCH_TOOLTIP = 'At least one branch must stay active.';
export const BRANCH_LOAD_ERROR_MESSAGE = "We couldn't load branches.";

type StatusFilter = 'all' | 'active' | 'inactive';
type SortDirection = 'ascending' | 'descending';

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const STATUS_OPTIONS: { code: StatusFilter; label: string }[] = [
  { code: 'all', label: 'All statuses' },
  { code: 'active', label: 'Active' },
  { code: 'inactive', label: 'Inactive' },
];

/**
 * Branch list (FR-14): client-side search, status filter and Branch-column
 * sort over the `GET /branches` result. Deactivate/reactivate are performed
 * directly from the row (D7: pre-disabled with the BR-06 tooltip whenever
 * the row is the organization's only active branch).
 */
@Component({
  selector: 'app-branch-list',
  imports: [FormsModule, ButtonDirective, InputText, Select, Skeleton, Tag, TooltipModule],
  templateUrl: './branch-list.html',
  styleUrl: './branch-list.scss',
})
export class BranchList {
  private readonly branchesService = inject(BranchesService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  readonly branches = input.required<readonly BranchListItem[]>();
  readonly loading = input(false);
  readonly loadError = input<ApiError | null>(null);
  readonly canManage = input.required<boolean>();

  readonly rowActivated = output<string>();
  readonly addBranch = output<void>();
  readonly changed = output<void>();
  readonly retry = output<void>();
  readonly unauthorized = output<void>();

  readonly statusOptions = STATUS_OPTIONS;
  readonly onlyActiveTooltip = ONLY_ACTIVE_BRANCH_TOOLTIP;
  readonly loadErrorMessage = BRANCH_LOAD_ERROR_MESSAGE;

  readonly search = signal('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly sortDirection = signal<SortDirection>('ascending');
  readonly mutatingId = signal<string | null>(null);

  readonly onlyActiveBranchId = computed(() => {
    const active = this.branches().filter((branch) => branch.isActive);
    return active.length === 1 ? active[0].id : null;
  });

  readonly filtered = computed(() => {
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
    return direction === 'ascending' ? sorted : sorted.reverse();
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

  onRowKeydown(event: KeyboardEvent, branch: BranchListItem): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.onRowActivate(branch);
    }
  }

  deactivate(branch: BranchListItem): void {
    if (this.mutatingId() !== null || this.isOnlyActive(branch)) {
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
      error: (error: unknown) => this.handleMutationFailed(error),
    });
  }

  private handleMutationFailed(error: unknown): void {
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
      this.messageService.add({ severity: 'error', summary: ONLY_ACTIVE_BRANCH_TOOLTIP });
      return;
    }
    this.messageService.add({
      severity: 'error',
      summary: apiError?.message ?? 'An unexpected error occurred. Please try again.',
    });
  }
}
