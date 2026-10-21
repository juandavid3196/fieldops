import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  LOAD_ERROR_MESSAGE,
  PUBLIC_PROTECTION_MESSAGE,
  PUBLIC_RATE_LIMITED_MESSAGE,
  PUBLIC_UNAVAILABLE_DETAIL,
  PUBLIC_UNAVAILABLE_MESSAGE,
} from '../../models/invoice.model';

export type NeutralKind = 'unavailable' | 'rate-limited' | 'error';

/** BR-34: no organization, invoice data or contact details, only generic copy. */
@Component({
  selector: 'app-public-neutral-state',
  imports: [TranslocoPipe, ButtonDirective],
  templateUrl: './public-neutral-state.html',
  styleUrl: './public-neutral-state.scss',
})
export class PublicNeutralState {
  readonly kind = input.required<NeutralKind>();
  readonly retry = output<void>();

  get unavailableTitle(): string {
    return PUBLIC_UNAVAILABLE_MESSAGE();
  }
  get unavailableDetail(): string {
    return PUBLIC_UNAVAILABLE_DETAIL();
  }
  get protection(): string {
    return PUBLIC_PROTECTION_MESSAGE();
  }
  get rateLimited(): string {
    return PUBLIC_RATE_LIMITED_MESSAGE();
  }
  get loadError(): string {
    return LOAD_ERROR_MESSAGE();
  }
}
