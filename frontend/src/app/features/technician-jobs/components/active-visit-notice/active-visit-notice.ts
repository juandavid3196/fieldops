import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ActiveVisitSummary } from '../../models/technician-visits.model';
import { formatInstantDateTime, statusLabelOf } from '../../utils/technician-format';

/**
 * BR-19: message, card of the active visit and Open active visit. Focus moves to the message when
 * the notice appears; the parent recreates it for every new 409.
 */
@Component({
  selector: 'app-active-visit-notice',
  imports: [TranslocoPipe, RouterLink],
  templateUrl: './active-visit-notice.html',
  styleUrl: './active-visit-notice.scss',
})
export class ActiveVisitNotice {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly message = input.required<string>();
  readonly visit = input.required<ActiveVisitSummary>();
  readonly timezone = input.required<string>();

  readonly statusText = computed(() => statusLabelOf(this.visit().status));
  readonly scheduledText = computed(() => {
    const start = this.visit().scheduledStart;
    return start === null ? null : formatInstantDateTime(start, this.timezone());
  });

  constructor() {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('p')?.focus(), {
      injector: inject(Injector),
    });
  }
}
