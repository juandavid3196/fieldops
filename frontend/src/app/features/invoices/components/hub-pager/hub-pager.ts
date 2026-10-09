import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { pageCount, showingText } from '../../utils/invoices-hub-format';

/** Footer of a hub list: "Showing <a> – <b> of <n> <noun>" and Previous/Next paging (BR-22). */
@Component({
  selector: 'app-hub-pager',
  imports: [ButtonDirective],
  template: `
    <div class="pager">
      <p class="pager__text">{{ text() }}</p>
      @if (pages() > 1) {
        <nav class="pager__nav" aria-label="Pagination">
          <button
            pButton
            type="button"
            severity="secondary"
            [outlined]="true"
            size="small"
            [disabled]="page() <= 1"
            (click)="pageChange.emit(page() - 1)"
          >
            Previous
          </button>
          <span>Page {{ page() }} of {{ pages() }}</span>
          <button
            pButton
            type="button"
            severity="secondary"
            [outlined]="true"
            size="small"
            [disabled]="page() >= pages()"
            (click)="pageChange.emit(page() + 1)"
          >
            Next
          </button>
        </nav>
      }
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
      display: flex;
      gap: 0.75rem;
      align-items: center;
    }
  `,
})
export class HubPager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly noun = input.required<string>();

  readonly pageChange = output<number>();

  readonly pages = computed(() => pageCount(this.total(), this.pageSize()));
  readonly text = computed(() =>
    this.total() === 0
      ? `Showing 0 of 0 ${this.noun()}`
      : showingText(this.page(), this.pageSize(), this.total(), this.noun()),
  );
}
