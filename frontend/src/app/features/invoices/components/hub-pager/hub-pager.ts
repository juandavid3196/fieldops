import { Component, computed, input, output } from '@angular/core';
import { Paginator, PaginatorState } from 'primeng/paginator';

import { showingText } from '../../utils/invoices-hub-format';

/** Footer of a hub list: "Showing <a> – <b> of <n> <noun>" and the page controls on the right (BR-22). */
@Component({
  selector: 'app-hub-pager',
  imports: [Paginator],
  template: `
    <div class="pager">
      <p class="pager__text">{{ text() }}</p>
      <p-paginator
        class="pager__nav"
        [first]="(page() - 1) * pageSize()"
        [rows]="pageSize()"
        [totalRecords]="total()"
        [pageLinkSize]="3"
        [showFirstLastIcon]="false"
        (onPageChange)="changePage($event)"
      />
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .pager {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
      justify-content: space-between;
      padding: 0.75rem 1rem;
      color: var(--fo-color-text-secondary);
    }

    .pager__text {
      margin: 0;
    }

    .pager__nav {
      margin-inline-start: auto;
      --p-paginator-padding: 0;
      --p-paginator-background: transparent;
    }
  `,
})
export class HubPager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly noun = input.required<string>();

  readonly pageChange = output<number>();

  readonly text = computed(() =>
    this.total() === 0
      ? `Showing 0 of 0 ${this.noun()}`
      : showingText(this.page(), this.pageSize(), this.total(), this.noun()),
  );

  changePage(event: PaginatorState): void {
    const page = (event.page ?? 0) + 1;
    if (page !== this.page()) {
      this.pageChange.emit(page);
    }
  }
}
