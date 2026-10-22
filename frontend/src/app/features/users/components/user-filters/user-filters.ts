import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TranslocoPipe } from '@jsverse/transloco';

import { BranchRef, MatrixRole, UserStatusFilter } from '../../models/users.model';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
import { tRole } from '../../../../core/i18n/translate';
interface Option<T> {
  readonly code: T;
  readonly label: string;
}

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const STATUS_OPTIONS: Option<UserStatusFilter | null>[] = localizedList(() => [
  { code: null, label: t('users.userFilters.allStatuses') },
  { code: 'active', label: t('common.active') },
  { code: 'pending_invitation', label: t('users.userFilters.pendingInvitation') },
  { code: 'suspended', label: t('users.userFilters.suspended') },
]);

/** Users filters (FR-04): search, Role, Branch access, Account status and Clear filters. */
@Component({
  selector: 'app-user-filters',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Select,
  ],
  templateUrl: './user-filters.html',
  styleUrl: './user-filters.scss',
})
export class UserFilters {
  readonly searchText = input.required<string>();
  readonly roleCode = input<string | null>(null);
  readonly branchId = input<string | null>(null);
  readonly status = input<UserStatusFilter | null>(null);
  readonly roles = input.required<readonly MatrixRole[]>();
  readonly branches = input.required<readonly BranchRef[]>();
  readonly hasFilters = input(false);

  readonly searchTextChange = output<string>();
  readonly roleChange = output<string | null>();
  readonly branchChange = output<string | null>();
  readonly statusChange = output<UserStatusFilter | null>();
  readonly cleared = output<void>();

  readonly statusOptions = STATUS_OPTIONS;
  readonly roleOptions = computed<Option<string | null>[]>(() => [
    { code: null, label: t('users.userFilters.allRoles') },
    ...this.roles().map((role) => ({ code: role.code, label: tRole(role.code, role.name) })),
  ]);
  readonly branchOptions = computed<Option<string | null>[]>(() => [
    { code: null, label: t('users.userFilters.allBranches') },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
}
