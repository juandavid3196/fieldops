import { Component, DestroyRef, computed, inject, input, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Subject, debounceTime } from 'rxjs';

import { durationLabel } from '../../../jobs/utils/work-order-format';
import { UnscheduledFilter, UnscheduledItem } from '../../models/schedule.model';
import { LoadStatus } from '../../services/schedule-page.store';
import { PRIORITY_ICONS, PRIORITY_TEXT } from '../../utils/dispatch-form';
import { formatPreferred, numberText } from '../../utils/schedule-range';

export const PANEL_ERROR_MESSAGE = "We couldn't load unscheduled visits.";
export const NO_MATCH_MESSAGE = 'No visits match your search.';
export const SEARCH_DEBOUNCE_MS = 300;

const FILTERS: readonly { readonly code: UnscheduledFilter; readonly label: string }[] = [
  { code: 'all', label: 'All' },
  { code: 'today', label: 'Today' },
  { code: 'overdue', label: 'Overdue' },
];
const EMPTY_TEXT: Readonly<Record<UnscheduledFilter, string>> = {
  all: 'No unscheduled visits.',
  today: 'No visits due today.',
  overdue: 'No overdue visits.',
};

/** Presentational unscheduled visits panel: filters, search, cards, Load more (BR-07). */
@Component({
  selector: 'app-unscheduled-panel',
  imports: [FormsModule, ButtonDirective, IconField, InputIcon, InputText, Message, Skeleton, Tag],
  templateUrl: './unscheduled-panel.html',
  styleUrl: './unscheduled-panel.scss',
})
export class UnscheduledPanel {
  private readonly searchInput = new Subject<string>();

  readonly items = input<readonly UnscheduledItem[]>([]);
  readonly total = input(0);
  readonly status = input<LoadStatus>('loading');
  readonly filter = input<UnscheduledFilter>('all');
  readonly searchText = input('');
  readonly loadingMore = input(false);
  readonly hasMore = input(false);
  readonly selectedVisitId = input<string | null>(null);
  readonly timezone = input('UTC');

  readonly filterChange = output<UnscheduledFilter>();
  readonly searchChange = output<string>();
  readonly selected = output<string>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();

  readonly filters = FILTERS;
  readonly errorMessage = PANEL_ERROR_MESSAGE;
  readonly priorityText = PRIORITY_TEXT;
  readonly priorityIcons = PRIORITY_ICONS;
  readonly skeletons = [0, 1, 2];

  readonly emptyText = computed(() =>
    this.searchText().trim().length > 0 ? NO_MATCH_MESSAGE : EMPTY_TEXT[this.filter()],
  );
  readonly cards = computed(() =>
    this.items().map((item) => {
      const window = formatPreferred(item.preferredStart, item.preferredEnd, this.timezone());
      const number = numberText(item.displayNumber);
      const visitOf =
        item.recurrenceCount === null
          ? null
          : `Visit ${item.visitNumber} of ${item.recurrenceCount}`;
      return {
        item,
        number,
        window,
        duration: durationLabel(item.estimatedDurationMinutes),
        visitOf,
        ariaLabel: [
          item.title,
          number,
          `${PRIORITY_TEXT[item.priority]} priority`,
          window,
          item.isOverdue ? 'Overdue' : null,
        ]
          .filter((part): part is string => part !== null)
          .join(', '),
      };
    }),
  );

  constructor() {
    this.searchInput
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((value) => this.searchChange.emit(value));
  }

  onSearch(value: string): void {
    this.searchInput.next(value);
  }
}
