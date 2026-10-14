import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BusinessAlert } from '../../models/business-overview.model';
import {
  ATTENTION_LIMIT,
  AttentionItem,
  AttentionLink,
  attentionItem,
} from '../../utils/business-view';

export const ATTENTION_EMPTY_TEXT = 'Nothing needs your attention.';
export const DISCLAIMER_TEXT =
  'Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, payroll taxes, depreciation, and other accounting expenses. They are not net income and not a substitute for accounting software.';

interface AttentionView extends AttentionItem {
  readonly path: readonly string[];
  readonly queryParams: Readonly<Record<string, string>> | undefined;
}

/** BR-18, BR-19: alerts with their actions (3 shown, rest expandable) beside the disclaimer. */
@Component({
  selector: 'app-owner-attention',
  imports: [RouterLink],
  templateUrl: './owner-attention.html',
  styleUrl: './owner-attention.scss',
})
export class OwnerAttention {
  readonly alerts = input.required<readonly BusinessAlert[]>();
  readonly currency = input.required<string>();
  readonly target = input.required<number>();
  /** Range and comparison of the page, kept by the branch drill-down link. */
  readonly keptQuery = input.required<Readonly<Record<string, string>>>();

  readonly expanded = signal(false);
  readonly emptyText = ATTENTION_EMPTY_TEXT;
  readonly disclaimer = DISCLAIMER_TEXT;

  readonly items = computed((): readonly AttentionView[] =>
    this.alerts().map((alert, index) => {
      const item = attentionItem(alert, index, this.currency(), this.target());
      return { ...item, ...this.destination(item.link) };
    }),
  );
  readonly visible = computed(() =>
    this.expanded() ? this.items() : this.items().slice(0, ATTENTION_LIMIT),
  );
  readonly canExpand = computed(() => this.items().length > ATTENTION_LIMIT);

  private destination(link: AttentionLink): {
    path: readonly string[];
    queryParams: Readonly<Record<string, string>> | undefined;
  } {
    switch (link.kind) {
      case 'invoices':
        return { path: ['/invoices'], queryParams: { status: 'overdue' } };
      case 'branch':
        return {
          path: ['/dashboard'],
          queryParams: { ...this.keptQuery(), branch: link.branchId },
        };
      case 'catalog':
        return { path: ['/admin/products-services'], queryParams: undefined };
      case 'team':
        return { path: ['/team'], queryParams: undefined };
    }
  }
}
