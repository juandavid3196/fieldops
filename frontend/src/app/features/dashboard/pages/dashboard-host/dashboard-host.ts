import { Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { canSeeBusinessOverview } from '../../../../core/config/dashboard-roles';
import { SessionService } from '../../../../core/services/session.service';
import { BusinessOverviewPage } from '../../../business-overview/pages/business-overview/business-overview';
import { DashboardPage } from '../dashboard/dashboard';

export type HostedView = 'business' | 'operations';

/**
 * business-overview BR-01: resolves which view `/dashboard` hosts. Only the Owner reaches the
 * Business view (default) or the Operations view (`view=operations`); an invalid `view` becomes
 * `business`. Every other role gets the Operations dashboard (or its forbidden state) and loses
 * any `view` parameter. Pages are instantiated only for the view that is shown.
 */
@Component({
  selector: 'app-dashboard-host',
  imports: [BusinessOverviewPage, DashboardPage],
  template: `
    @if (view() === 'business') {
      <app-business-overview />
    } @else {
      <app-dashboard />
    }
  `,
})
export class DashboardHost {
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  private readonly owner = computed(() =>
    canSeeBusinessOverview(this.sessionService.session()?.role.code),
  );
  readonly view = computed((): HostedView =>
    this.owner() && this.queryParams().get('view') !== 'operations' ? 'business' : 'operations',
  );

  constructor() {
    effect(() => {
      const requested = this.queryParams().get('view');
      const owner = this.owner();
      untracked(() => {
        if (requested === null) {
          return;
        }
        if (!owner) {
          this.replace(null);
        } else if (requested !== 'business' && requested !== 'operations') {
          this.replace('business');
        }
      });
    });
  }

  private replace(view: HostedView | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
