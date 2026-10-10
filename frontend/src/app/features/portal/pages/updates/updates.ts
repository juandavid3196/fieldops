import { Component, DestroyRef, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { tap } from 'rxjs';

import { updateLink } from '../../components/updates-card/updates-card';
import { PortalState } from '../../components/portal-state/portal-state';
import { UpdatesResponse } from '../../models/portal.model';
import { PortalUpdatesStore } from '../../services/portal-updates.store';
import { updateTimeLabel } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';

/** All updates (BR-24, BR-25): opening the page marks them seen, so the bell shows no count. */
@Component({
  selector: 'app-portal-updates',
  imports: [PortalState, RouterLink],
  templateUrl: './updates.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class PortalUpdates {
  private readonly store = inject(PortalUpdatesStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly resource = new PortalResource<UpdatesResponse>(
    (response) => response.items.length === 0,
  );
  readonly rows = computed(() => {
    const now = new Date();
    return (this.resource.data()?.items ?? []).map((update) => ({
      update,
      link: updateLink(update),
      time: updateTimeLabel(update.occurredAt, now),
    }));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.resource.load(
      this.store.load().pipe(
        // Seen after the list is loaded, so the unread dots of this visit stay visible.
        tap(() =>
          this.store
            .markSeen()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ error: () => undefined }),
        ),
      ),
    );
  }
}
