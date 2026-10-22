import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Subscription } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { TechnicianDetail } from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import { PROFILE_STATUS_LABELS, profileStatusSeverity } from '../../utils/team-format';
import { SCHEDULE_PATH, TechnicianProfile } from '../technician-profile/technician-profile';

import { t } from '../../../../core/i18n/translate';
export const TECHNICIAN_UNAVAILABLE_MESSAGE = (): string =>
  t('team.technicianDrawer.technicianUnavailableMessage');
export const DETAIL_ERROR_MESSAGE = (): string => t('team.technicianDrawer.detailErrorMessage');

/** BR-13 detail drawer. The page owns refreshes, account actions and the 401 redirect. */
@Component({
  selector: 'app-technician-drawer',
  imports: [
    TranslocoPipe,
    RouterLink,
    ButtonDirective,
    Message,
    Skeleton,
    Tag,
    DrawerShell,
    TechnicianProfile,
  ],
  templateUrl: './technician-drawer.html',
  styleUrl: './technician-drawer.scss',
})
export class TechnicianDrawer {
  private readonly team = inject(TeamService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly technicianId = input<string | null>(null);
  readonly canMutate = input(false);
  readonly canViewSettings = input(false);
  readonly busy = input(false);

  readonly closed = output<void>();
  readonly editRequested = output<string>();
  readonly linkRequested = output<string>();
  readonly unlinkRequested = output<string>();
  readonly unauthorized = output<void>();

  readonly titleId = 'technician-drawer-title';
  readonly schedulePath = SCHEDULE_PATH;
  readonly profileSeverity = profileStatusSeverity;
  get unavailableMessage(): string {
    return TECHNICIAN_UNAVAILABLE_MESSAGE();
  }
  get errorMessage(): string {
    return DETAIL_ERROR_MESSAGE();
  }

  readonly detail = signal<TechnicianDetail | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly unavailable = signal(false);
  readonly fullName = computed(() => {
    const detail = this.detail();
    return detail === null
      ? t('team.technicianDrawer.technician')
      : `${detail.firstName} ${detail.lastName}`.trim();
  });
  readonly profileStatusLabel = computed(() => {
    return PROFILE_STATUS_LABELS[this.detail()?.profileStatus ?? 'active'];
  });

  private request: Subscription | null = null;

  constructor() {
    effect(() => {
      const id = this.technicianId();
      if (this.open() && id !== null) {
        untracked(() => this.load(id));
      }
    });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }

  /** Reloads the open profile (after an edit, link change or status change). */
  reload(): void {
    const id = this.technicianId();
    if (this.open() && id !== null) {
      this.load(id, true);
    }
  }

  retry(): void {
    const id = this.technicianId();
    if (id !== null) {
      this.load(id);
    }
  }

  private load(id: string, keepContent = false): void {
    this.request?.unsubscribe();
    if (!keepContent) {
      this.detail.set(null);
      this.loading.set(true);
    }
    this.failed.set(false);
    this.unavailable.set(false);
    this.request = this.team.get(id).subscribe({
      next: (detail) => {
        this.loading.set(false);
        this.detail.set(detail);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (kind === 'not-found') {
          this.detail.set(null);
          this.unavailable.set(true);
        } else {
          this.failed.set(true);
        }
      },
    });
  }
}
