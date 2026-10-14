import { Component, computed, input } from '@angular/core';

import { BranchRow, BranchTotals } from '../../models/business-overview.model';
import {
  BELOW_TARGET_TEXT,
  belowTarget,
  money,
  percentOne,
  percentWhole,
  ratingText,
} from '../../utils/business-view';
import { InfoHint } from '../info-hint/info-hint';

export const MARGIN_COLUMN_HINT = 'Estimated margin = estimated gross profit ÷ revenue.';

/** BR-15, BR-09: one row per branch, a Total row from two rows and the below-target marking. */
@Component({
  selector: 'app-branch-performance',
  imports: [InfoHint],
  templateUrl: './branch-performance.html',
  styleUrl: './branch-performance.scss',
})
export class BranchPerformance {
  readonly branches = input.required<readonly BranchRow[]>();
  readonly totals = input.required<BranchTotals | null>();
  readonly currency = input.required<string>();
  readonly target = input.required<number>();

  readonly hint = MARGIN_COLUMN_HINT;
  readonly warningText = BELOW_TARGET_TEXT;

  readonly rows = computed(() =>
    this.branches().map((branch) => ({
      id: branch.branchId,
      name: branch.isActive ? branch.name : `${branch.name} (inactive)`,
      revenue: money(branch.revenue, this.currency()),
      margin: percentOne(branch.marginPercent),
      below: belowTarget(branch.marginPercent, this.target()),
      jobs: String(branch.jobsCompleted),
      onTime: percentWhole(branch.onTimePercent),
      rating: ratingText(branch.rating),
    })),
  );
  readonly total = computed(() => {
    const value = this.totals();
    return value === null || this.branches().length < 2
      ? null
      : {
          revenue: money(value.revenue, this.currency()),
          margin: percentOne(value.marginPercent),
          below: belowTarget(value.marginPercent, this.target()),
          jobs: String(value.jobsCompleted),
          onTime: percentWhole(value.onTimePercent),
        };
  });
}
