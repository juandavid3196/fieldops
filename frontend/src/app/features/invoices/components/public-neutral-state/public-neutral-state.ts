import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

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
  imports: [ButtonDirective],
  templateUrl: './public-neutral-state.html',
  styleUrl: './public-neutral-state.scss',
})
export class PublicNeutralState {
  readonly kind = input.required<NeutralKind>();
  readonly retry = output<void>();

  readonly unavailableTitle = PUBLIC_UNAVAILABLE_MESSAGE;
  readonly unavailableDetail = PUBLIC_UNAVAILABLE_DETAIL;
  readonly protection = PUBLIC_PROTECTION_MESSAGE;
  readonly rateLimited = PUBLIC_RATE_LIMITED_MESSAGE;
  readonly loadError = LOAD_ERROR_MESSAGE;
}
