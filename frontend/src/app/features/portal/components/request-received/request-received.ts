import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { PortalStatusChip } from '../portal-status-chip/portal-status-chip';

/** Portal request confirmation (BR-28): number, BR-39 status, View request and Back to home. */
@Component({
  selector: 'app-request-received',
  imports: [ButtonDirective, PortalStatusChip, RouterLink],
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
    return name === null ? 'Our team' : `The ${name} team`;
  });

  readonly copied = signal(false);

  copy(): void {
    navigator.clipboard?.writeText(this.requestNumber()).then(
      () => this.copied.set(true),
      () => undefined,
    );
  }
}
