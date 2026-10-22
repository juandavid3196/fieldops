import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { PortalStatusChip } from '../portal-status-chip/portal-status-chip';

import { t } from '../../../../core/i18n/translate';
/** Portal request confirmation (BR-28): number, BR-39 status, View request and Back to home. */
@Component({
  selector: 'app-request-received',
  imports: [TranslocoPipe, ButtonDirective, PortalStatusChip, RouterLink],
  templateUrl: './request-received.html',
  styleUrl: './request-received.scss',
})
export class RequestReceived {
  readonly requestNumber = input.required<string>();
  readonly requestId = input<string | undefined>(undefined);
  readonly propertyName = input<string | null>(null);
  readonly organizationName = input<string | null>(null);

  protected readonly team = computed(() => {
    const name = this.organizationName();
    return name === null
      ? t('portal.requestReceived.ourTeam')
      : t('portal.requestReceived.theTeam', { name: name });
  });

  readonly copied = signal(false);

  copy(): void {
    navigator.clipboard?.writeText(this.requestNumber()).then(
      () => this.copied.set(true),
      () => undefined,
    );
  }
}
