import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import {
  QUEUE_EMPTY_MESSAGE,
  QUEUE_ERROR_MESSAGE,
  QUEUE_FILTERED_EMPTY_MESSAGE,
  QueueFilters,
  QueueResponse,
  QueueTab,
} from '../../models/billing-review.model';
import { formatDateTime, money, varianceBadge } from '../../utils/billing-review-format';

const TABS: readonly { readonly code: QueueTab; readonly label: string }[] = [
  { code: 'all', label: 'All' },
  { code: 'variances', label: 'Variances' },
  { code: 'ready', label: 'Ready' },
];

/** Queue panel (BR-23): title, tabs with counts, selectable items, paging and its states. */
@Component({
  selector: 'app-review-queue',
  imports: [ButtonDirective, Skeleton],
  templateUrl: './review-queue.html',
  styleUrl: './review-queue.scss',
})
export class ReviewQueue {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly filters = input.required<QueueFilters>();
  readonly queue = input<QueueResponse | null>(null);
  readonly timezone = input('UTC');
  readonly loading = input(false);
  readonly failed = input(false);
  /** Any filter or search differs from the defaults. */
  readonly filtered = input(false);
  readonly selectedId = input<string | null>(null);

  readonly filtersChange = output<Partial<QueueFilters>>();
  readonly selected = output<string>();
  readonly retry = output<void>();
  readonly clearFilters = output<void>();

  readonly tabs = TABS;
  readonly errorMessage = QUEUE_ERROR_MESSAGE;
  readonly pageCount = computed(() => {
    const queue = this.queue();
    return queue === null ? 1 : Math.max(1, Math.ceil(queue.total / queue.pageSize));
  });
  readonly rows = computed(() =>
    (this.queue()?.items ?? []).map((item) => ({
      item,
      total: money(item.approvedTotal, item.currency),
      completed: `Completed ${formatDateTime(item.completedAt, this.timezone())}`,
      badge: varianceBadge(item.laborVariance, item.materialVariance),
      hasVariance: item.laborVariance !== 'none' || item.materialVariance,
    })),
  );
  readonly emptyMessage = computed(() =>
    this.filtered() || this.filters().tab !== 'all'
      ? QUEUE_FILTERED_EMPTY_MESSAGE
      : QUEUE_EMPTY_MESSAGE,
  );

  count(tab: QueueTab): string {
    const tabs = this.queue()?.tabs;
    return tabs === undefined ? '—' : String(tabs[tab]);
  }

  /** Arrow keys move the selection between tabs (roving tabindex). */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }
    event.preventDefault();
    const next = (index + step + TABS.length) % TABS.length;
    this.filtersChange.emit({ tab: TABS[next].code });
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }

  go(page: number): void {
    this.filtersChange.emit({ page: Math.min(Math.max(1, page), this.pageCount()) });
  }
}
