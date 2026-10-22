import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  ACTIVITY_PERIODS,
  ActivityPeriod,
  ActivityType,
  RegionState,
} from '../../models/customer.model';
import { ActivityGroup } from '../../utils/customer-activity';
import { recordLink } from '../../utils/record-link';

import { t } from '../../../../core/i18n/translate';

/**
 * Day-grouped activity timeline with Type and Period filters and "Load older activity" (BR-11,
 * BR-12). Presentational: the page owns the data, the cursor and the load states.
 */
@Component({
  selector: 'app-activity-timeline',
  imports: [
    TranslocoPipe,
    NgTemplateOutlet,
    RouterLink,
    FormsModule,
    ButtonDirective,
    Message,
    Select,
    Skeleton,
  ],
  templateUrl: './activity-timeline.html',
  styleUrl: './activity-timeline.scss',
})
export class ActivityTimeline {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly state = input.required<RegionState<unknown>>();
  readonly groups = input.required<readonly ActivityGroup[]>();
  readonly types = input.required<readonly ActivityType[]>();
  readonly type = input.required<ActivityType | 'all'>();
  readonly period = input.required<ActivityPeriod>();
  readonly hasMore = input(false);
  readonly loadingOlder = input(false);
  readonly olderError = input(false);
  /** First event of the block just appended; it takes focus (accessibility). */
  readonly focusId = input<string | null>(null);

  readonly typeChange = output<ActivityType | 'all'>();
  readonly periodChange = output<ActivityPeriod>();
  readonly loadOlder = output<void>();
  readonly retry = output<void>();

  readonly typeOptions = computed(() => {
    const codes: (ActivityType | 'all')[] = ['all', ...this.types()];
    return codes.map((code) => ({ code, label: t(`customers.activityTab.type.${code}`) }));
  });
  readonly periodOptions = computed(() =>
    ACTIVITY_PERIODS.map((code) => ({ code, label: t(`customers.activityTab.period.${code}`) })),
  );

  readonly link = recordLink;

  constructor() {
    effect(() => {
      const id = this.focusId();
      if (id !== null) {
        // Runs after the change-detection pass that renders the appended block.
        queueMicrotask(() =>
          Array.from(this.host.querySelectorAll<HTMLElement>('[data-event-id]'))
            .find((element) => element.dataset['eventId'] === id)
            ?.focus(),
        );
      }
    });
  }
}
