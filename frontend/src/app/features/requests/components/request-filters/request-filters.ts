import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import {
  CreatedRange,
  RequestFilters,
  RequestOptions,
  RequestSource,
  URGENCY_LABELS,
  Urgency,
} from '../../models/requests.model';

interface Option<T> {
  readonly code: T;
  readonly label: string;
}

const ALL = 'all';

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const PRIORITY_OPTIONS: Option<Urgency | typeof ALL>[] = [
  { code: ALL, label: 'All' },
  ...(Object.keys(URGENCY_LABELS) as Urgency[]).map((code) => ({
    code,
    label: URGENCY_LABELS[code],
  })),
];
const SOURCE_OPTIONS: Option<RequestSource | typeof ALL>[] = [
  { code: ALL, label: 'All' },
  { code: 'public_form', label: 'Public form' },
  { code: 'internal', label: 'Internal' },
];
const DATE_OPTIONS: Option<CreatedRange | typeof ALL>[] = [
  { code: ALL, label: 'All' },
  { code: 'today', label: 'Today' },
  { code: '7d', label: 'Last 7 days' },
  { code: '30d', label: 'Last 30 days' },
];

/** Assignee / Service / Priority / Source / Date filters, search and New request (BR-06). */
@Component({
  selector: 'app-request-filters',
  imports: [FormsModule, ButtonDirective, IconField, InputIcon, InputText, Select],
  templateUrl: './request-filters.html',
  styleUrl: './request-filters.scss',
})
export class RequestFiltersBar {
  readonly filters = input.required<RequestFilters>();
  readonly searchText = input.required<string>();
  readonly options = input<RequestOptions | null>(null);
  readonly disabled = input(false);
  readonly canCreate = input(false);

  readonly filterChange = output<Partial<RequestFilters>>();
  readonly searchTextChange = output<string>();
  readonly newRequest = output<void>();

  readonly priorityOptions = PRIORITY_OPTIONS;
  readonly sourceOptions = SOURCE_OPTIONS;
  readonly dateOptions = DATE_OPTIONS;

  readonly assigneeOptions = computed<Option<string>[]>(() => [
    { code: ALL, label: 'All' },
    { code: 'unassigned', label: 'Unassigned' },
    ...(this.options()?.assignees ?? []).map((a) => ({ code: a.userId, label: a.name })),
  ]);
  readonly serviceOptions = computed<Option<string>[]>(() => [
    { code: ALL, label: 'All' },
    ...(this.options()?.categories ?? []).map((c) => ({ code: c.id, label: c.name })),
  ]);

  /** `all` maps back to the "no filter" value. */
  none<T extends string>(value: T | typeof ALL): T | null {
    return value === ALL ? null : value;
  }
}
