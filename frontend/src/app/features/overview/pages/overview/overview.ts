import { Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { SessionService } from '../../../../core/services/session.service';

import { t } from '../../../../core/i18n/translate';
import { CodeLabelPipe } from '../../../../core/i18n/code-label.pipe';
/** Minimal authenticated landing page (FR-17); the app shell provides `main`. */
@Component({
  selector: 'app-overview',
  imports: [CodeLabelPipe, TranslocoPipe],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  readonly session = inject(SessionService).session;
  readonly heading = computed(() => {
    const firstName = this.session()?.user.firstName;
    return firstName
      ? t('overview.overview.welcome', { firstName: firstName })
      : t('overview.overview.welcome2');
  });
}
