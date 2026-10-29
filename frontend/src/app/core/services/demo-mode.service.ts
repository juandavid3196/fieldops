import { Injectable, computed, inject, signal } from '@angular/core';

import { PortalSessionService } from './portal-session.service';
import { SessionService } from './session.service';

/** Machine-readable `code` of the API refusal for a write in a read-only demo. */
export const DEMO_READ_ONLY_CODE = 'demo_read_only';

/**
 * Read-only demo state. The flag comes from the session the API returned and only drives display: the API refuses the
 * writes itself, so nothing here grants or removes a permission.
 */
@Injectable({ providedIn: 'root' })
export class DemoModeService {
  private readonly internal = inject(SessionService);
  private readonly portal = inject(PortalSessionService);
  private readonly refusalCount = signal(0);

  readonly active = computed(
    () =>
      this.internal.session()?.demoReadOnly === true ||
      this.portal.session()?.demoReadOnly === true,
  );
  /** Incremented after every write the API refused as read-only; the shell toast reacts to it. */
  readonly refusals = this.refusalCount.asReadonly();

  notifyRefusal(): void {
    this.refusalCount.update((count) => count + 1);
  }
}
