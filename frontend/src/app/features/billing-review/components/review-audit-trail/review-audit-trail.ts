import { Component, computed, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import { BillingReviewDetail } from '../../models/billing-review.model';
import { formatInstantDate, formatInstantTime } from '../../utils/billing-review-format';

type Category =
  | 'status'
  | 'completion'
  | 'evidence'
  | 'materials'
  | 'time'
  | 'travel'
  | 'schedule'
  | 'creation'
  | 'billing'
  | 'other';

const CATEGORY_LABELS: Readonly<Record<Category, string>> = {
  status: 'Status',
  completion: 'Completion',
  evidence: 'Evidence',
  materials: 'Materials',
  time: 'Time',
  travel: 'Travel',
  schedule: 'Schedule',
  creation: 'Creation',
  billing: 'Billing',
  other: 'Other',
};

/** Backend BR-12 audit labels → category and icon; unknown labels fall back to "Other". */
const KNOWN: Readonly<Record<string, { readonly category: Category; readonly icon: string }>> = {
  'Work order created': { category: 'creation', icon: 'pi-file' },
  'Work order status changed': { category: 'status', icon: 'pi-cog' },
  'Billing note updated': { category: 'billing', icon: 'pi-pencil' },
  'Marked for follow-up': { category: 'billing', icon: 'pi-flag' },
  'Follow-up removed': { category: 'billing', icon: 'pi-flag' },
  'Visit scheduled': { category: 'schedule', icon: 'pi-calendar' },
  'Technician on the way': { category: 'travel', icon: 'pi-send' },
  'Technician arrived': { category: 'travel', icon: 'pi-map-marker' },
  'Job started': { category: 'time', icon: 'pi-play' },
  'Job paused': { category: 'time', icon: 'pi-pause' },
  'Job resumed': { category: 'time', icon: 'pi-play' },
  'Material recorded': { category: 'materials', icon: 'pi-box' },
  'Photo added': { category: 'evidence', icon: 'pi-image' },
  'Photo removed': { category: 'evidence', icon: 'pi-image' },
  'Job completed': { category: 'completion', icon: 'pi-check' },
};

interface Row {
  readonly label: string;
  readonly actorName: string;
  readonly category: Category;
  readonly icon: string;
  readonly tag: string;
  readonly date: string;
  readonly time: string;
}

let nextId = 0;

/** Audit trail tab (BR-12): read-only activity timeline grouped by organization date. */
@Component({
  selector: 'app-review-audit-trail',
  imports: [FormsModule, IconField, InputIcon, InputText, Select],
  templateUrl: './review-audit-trail.html',
  styleUrl: './review-audit-trail.scss',
})
export class ReviewAuditTrail {
  readonly entries = input.required<BillingReviewDetail['auditTrail']>();
  readonly timezone = input.required<string>();

  protected readonly id = `review-audit-${nextId++}`;
  protected readonly search = signal('');
  protected readonly category = signal<Category | 'all'>('all');

  private readonly rows = computed((): Row[] =>
    this.entries().map((entry) => {
      const known = KNOWN[entry.label] ?? { category: 'other' as const, icon: 'pi-circle' };
      return {
        label: entry.label,
        actorName: entry.actorName,
        ...known,
        tag: CATEGORY_LABELS[known.category],
        date: formatInstantDate(entry.occurredAt, this.timezone()),
        time: formatInstantTime(entry.occurredAt, this.timezone()),
      };
    }),
  );

  protected readonly categoryOptions = computed(() => [
    { code: 'all', label: 'All activity' },
    ...[...new Set(this.rows().map((row) => row.category))].map((code) => ({
      code,
      label: CATEGORY_LABELS[code],
    })),
  ]);

  protected readonly days = computed(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.category();
    const days: { date: string; rows: Row[] }[] = [];
    for (const row of this.rows()) {
      if (category !== 'all' && row.category !== category) continue;
      if (term && !`${row.label} ${row.actorName}`.toLowerCase().includes(term)) continue;
      const last = days.at(-1);
      if (last?.date === row.date) {
        last.rows.push(row);
      } else {
        days.push({ date: row.date, rows: [row] });
      }
    }
    return days;
  });
}
