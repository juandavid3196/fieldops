import { Component, computed, inject } from '@angular/core';

import { SessionService } from '../../../../core/services/session.service';

/** Minimal authenticated landing page (FR-17); the app shell provides `main`. */
@Component({
  selector: 'app-overview',
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  readonly session = inject(SessionService).session;
  readonly heading = computed(() => {
    const firstName = this.session()?.user.firstName;
    return firstName ? `Welcome, ${firstName}` : 'Welcome';
  });
}
