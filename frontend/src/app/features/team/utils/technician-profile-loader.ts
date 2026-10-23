import { DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { isApiError } from '../../../core/models/api-error.model';
import { SessionService } from '../../../core/services/session.service';
import { handleUnauthorized } from '../../organizations/utils/handle-unauthorized';
import { TechnicianDetail } from '../models/team.model';
import { TeamService } from '../services/team.service';

/** Roles with `TeamView` (UX only; the backend is authoritative). */
const TEAM_VIEW_ROLES: readonly string[] = ['owner', 'operations_manager', 'dispatcher'];

/**
 * State of the profile request shared by Overview and Job history (BR-21, BR-22): roles without
 * `TeamView` get the forbidden state and no requests; `404` is the not-available state. Call in an
 * injection context.
 */
export function injectProfileLoader() {
  const team = inject(TeamService);
  const session = inject(SessionService);
  const router = inject(Router);
  const destroyRef = inject(DestroyRef);
  const params = toSignal(inject(ActivatedRoute).paramMap);

  const technicianId = computed(() => params()?.get('technicianId') ?? '');
  const serverForbidden = signal(false);
  const forbidden = computed(
    () => !TEAM_VIEW_ROLES.includes(session.session()?.role.code ?? '') || serverForbidden(),
  );
  const notFound = signal(false);
  const failed = signal(false);
  const loading = signal(false);
  const detail = signal<TechnicianDetail | null>(null);
  const sessionExpired = signal(false);

  /** 401 leaves to Sign In, 403 shows the forbidden state, 404 the not-available state. */
  function handleAccessFailure(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      handleUnauthorized(router, sessionExpired);
      return true;
    }
    if (kind === 'forbidden') {
      serverForbidden.set(true);
      return true;
    }
    if (kind === 'not-found') {
      notFound.set(true);
      return true;
    }
    return false;
  }

  function load(): void {
    if (forbidden()) {
      return;
    }
    loading.set(true);
    failed.set(false);
    notFound.set(false);
    team
      .get(technicianId())
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe({
        next: (value) => {
          detail.set(value);
          loading.set(false);
        },
        error: (error: unknown) => {
          loading.set(false);
          if (!handleAccessFailure(error)) {
            failed.set(true);
          }
        },
      });
  }

  return { technicianId, forbidden, notFound, failed, loading, detail, load, handleAccessFailure };
}
