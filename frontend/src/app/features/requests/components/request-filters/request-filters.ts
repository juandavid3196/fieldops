import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  CreatedRange,
  RequestFilters,
  RequestOptions,
  RequestSource,
  URGENCY_LABELS,
  Urgency,
} from '../../models/requests.model';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
interface Option<T> {
  readonly code: T;
  readonly label: string;
}

const ALL = 'all';

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const PRIORITY_OPTIONS: Option<Urgency | typeof ALL>[] = localizedList(() => [
  { code: ALL, label: t('requests.requestFilters.all') },
  ...(Object.keys(URGENCY_LABELS) as Urgency[]).map((code) => ({
    code,
    label: URGENCY_LABELS[code],
  })),
]);
const SOURCE_OPTIONS: Option<RequestSource | typeof ALL>[] = localizedList(() => [
  { code: ALL, label: t('requests.requestFilters.all') },
  { code: 'public_form', label: t('requests.requestFilters.publicForm') },
  { code: 'internal', label: t('requests.requestFilters.internal') },
]);
const DATE_OPTIONS: Option<CreatedRange | typeof ALL>[] = localizedList(() => [
  { code: ALL, label: t('requests.requestFilters.all') },
  { code: 'today', label: t('requests.requestFilters.today') },
  { code: '7d', label: t('requests.requestFilters.last7Days') },
  { code: '30d', label: t('requests.requestFilters.last30Days') },
]);

/** Assignee / Service / Priority / Source / Date filters, search and New request (BR-06). */
@Component({
  selector: 'app-request-filters',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Select,
  ],
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
    { code: ALL, label: t('requests.requestFilters.all') },
    { code: 'unassigned', label: t('requests.requestFilters.unassigned') },
    ...(this.options()?.assignees ?? []).map((a) => ({ code: a.userId, label: a.name })),
  ]);
  readonly serviceOptions = computed<Option<string>[]>(() => [
    { code: ALL, label: t('requests.requestFilters.all') },
    ...(this.options()?.categories ?? []).map((c) => ({ code: c.id, label: c.name })),
  ]);

  /** `all` maps back to the "no filter" value. */
  none<T extends string>(value: T | typeof ALL): T | null {
    return value === ALL ? null : value;
  }
}
