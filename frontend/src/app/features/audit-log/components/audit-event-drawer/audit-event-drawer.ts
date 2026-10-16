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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { Subject, catchError, of, switchMap, tap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  AUDIT_ALL_BRANCHES,
  AUDIT_COPIED_MESSAGE,
  AUDIT_DETAIL_ERROR_MESSAGE,
  AUDIT_DETAIL_GONE_MESSAGE,
  AuditDetail,
} from '../../models/audit-log.model';
import { AuditLogService } from '../../services/audit-log.service';
import {
  IMPACT_ICONS,
  IMPACT_LABELS,
  auditTargetRoute,
  drawerDateTime,
  initials,
} from '../../utils/audit-log-format';

type DetailState =
  | { readonly status: 'idle' | 'loading' | 'error' | 'gone' }
  | { readonly status: 'ready'; readonly detail: AuditDetail };

/** Audit event drawer (BR-22): a modal labelled dialog; Escape or close returns focus to View. */
@Component({
  selector: 'app-audit-event-drawer',
  imports: [ButtonDirective, DrawerShell, RouterLink, Skeleton],
  templateUrl: './audit-event-drawer.html',
  styleUrl: './audit-event-drawer.scss',
})
export class AuditEventDrawer {
  private readonly api = inject(AuditLogService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  /** Numeric id of the event to show; `null` closes the drawer. */
  readonly eventId = input<number | null>(null);

  readonly closed = output<void>();
  readonly relatedRequested = output<AuditDetail>();
  readonly forbidden = output<void>();
  readonly sessionExpired = output<void>();

  readonly titleId = 'audit-drawer-title';
  readonly goneMessage = AUDIT_DETAIL_GONE_MESSAGE;
  readonly errorMessage = AUDIT_DETAIL_ERROR_MESSAGE;
  readonly allBranches = AUDIT_ALL_BRANCHES;
  readonly open = computed(() => this.eventId() !== null);
  readonly state = signal<DetailState>({ status: 'idle' });
  readonly copied = signal('');

  readonly view = computed(() => {
    const state = this.state();
    if (state.status !== 'ready') {
      return null;
    }
    const detail = state.detail;
    return {
      detail,
      when: drawerDateTime(detail.occurredAt, detail.timeZone),
      initials: initials(detail.actor.name),
      impactIcon: IMPACT_ICONS[detail.impact],
      impactLabel: IMPACT_LABELS[detail.impact],
      route: auditTargetRoute(detail.target),
    };
  });

  private readonly load$ = new Subject<number>();

  constructor() {
    this.load$
      .pipe(
        switchMap((id) => {
          this.state.set({ status: 'loading' });
          return this.api.detail(id).pipe(
            tap((detail) => this.state.set({ status: 'ready', detail })),
            catchError((error: unknown) => {
              const kind = isApiError(error) ? error.kind : null;
              if (kind === 'unauthorized') {
                this.sessionExpired.emit();
              } else if (kind === 'forbidden') {
                this.forbidden.emit();
              }
              this.state.set({ status: kind === 'not-found' ? 'gone' : 'error' });
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    effect(() => {
      const id = this.eventId();
      if (id !== null) {
        untracked(() => {
          this.copied.set('');
          this.load$.next(id);
        });
      }
    });
  }

  retry(): void {
    const id = this.eventId();
    if (id !== null) {
      this.load$.next(id);
    }
  }

  copy(eventId: string): void {
    void navigator.clipboard?.writeText(eventId).then(() => {
      this.copied.set(AUDIT_COPIED_MESSAGE);
      this.messages.add({ severity: 'success', summary: AUDIT_COPIED_MESSAGE });
    });
  }
}
