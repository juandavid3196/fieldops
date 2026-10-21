import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Select, SelectFilterEvent } from 'primeng/select';
import { Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { TeamOption } from '../../models/team.model';
import { TeamService } from '../../services/team.service';

import { t } from '../../../../core/i18n/translate';
const SEARCH_DEBOUNCE_MS = 300;

/**
 * Searchable select of active in-scope technicians (reuses `GET /team/technicians`) shared by
 * the Edit availability, Time off and Edit skills drawers (BR-08, BR-13, BR-20).
 */
@Component({
  selector: 'app-technician-picker',
  imports: [TranslocoPipe, FormsModule, Select],
  template: `
    <p-select
      appendTo="body"
      fluid
      [placeholder]="'team.technicianPicker.selectTechnician' | transloco"
      filterPlaceholder="Search technicians"
      optionLabel="name"
      optionValue="id"
      filterBy="name"
      [inputId]="inputId()"
      [ariaLabel]="labelValue()"
      [filter]="true"
      [resetFilterOnHide]="true"
      [options]="choices()"
      [loading]="loading()"
      [disabled]="disabled()"
      [ngModel]="value()"
      (ngModelChange)="selected.emit($event)"
      (onFilter)="onFilter($event)"
      emptyFilterMessage="No technicians match."
    />
  `,
})
export class TechnicianPicker {
  private readonly team = inject(TeamService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searches = new Subject<string>();

  readonly inputId = input.required<string>();
  readonly label = input<string | null>(null);
  readonly labelValue = computed(() => this.label() ?? t('team.technicianPicker.technician'));
  readonly value = input<string | null>(null);
  /** Keeps the current technician selectable when it is not on the loaded page. */
  readonly current = input<TeamOption | null>(null);
  readonly disabled = input(false);

  readonly selected = output<string>();

  private readonly options = signal<TeamOption[]>([]);
  readonly loading = signal(true);
  readonly choices = computed<TeamOption[]>(() => {
    const current = this.current();
    const options = this.options();
    return current !== null && !options.some((option) => option.id === current.id)
      ? [current, ...options]
      : options;
  });

  constructor() {
    this.searches
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        tap(() => this.loading.set(true)),
        switchMap((search) => this.query(search)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => this.apply(options));
    this.query('')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((options) => this.apply(options));
  }

  onFilter(event: SelectFilterEvent): void {
    this.searches.next(event.filter ?? '');
  }

  private apply(options: TeamOption[]): void {
    this.options.set(options);
    this.loading.set(false);
  }

  private query(search: string) {
    return this.team
      .list({
        search,
        branchId: null,
        skillId: null,
        status: 'all_active',
        accountLink: 'all',
        sort: 'name_asc',
        period: 'today',
        page: 1,
      })
      .pipe(
        switchMap((response) =>
          of(response.items.map((row) => ({ id: row.id, name: row.fullName }))),
        ),
        catchError(() => of<TeamOption[]>([])),
      );
  }
}
