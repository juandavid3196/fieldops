import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Subject, Subscription, debounceTime, filter, map } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import {
  AvailabilityFilter,
  AvailabilityPage,
  AvailabilityRow,
} from '../../models/team-planning.model';
import { TEAM_PAGE_SIZE, TeamOption } from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import {
  addDays,
  cellText,
  dayHeader,
  initials,
  weekDates,
  weekRangeLabel,
  weeklyHours,
} from '../../utils/team-planning';
import { AvailabilityDrawer } from '../availability-drawer/availability-drawer';
import { TeamTabs } from '../team-tabs/team-tabs';
import { TimeOffDrawer } from '../time-off-drawer/time-off-drawer';

export const AVAILABILITY_ERROR_MESSAGE = "We couldn't load availability. Try again.";
export const EMPTY_TECHNICIANS_MESSAGE = 'No active technicians yet.';
export const FILTERED_EMPTY_MESSAGE = 'No technicians match these filters.';
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];

interface Choice {
  readonly code: string;
  readonly label: string;
}

const AVAILABILITY_OPTIONS: Choice[] = [
  { code: 'all', label: 'All availability' },
  { code: 'available', label: 'Available this week' },
  { code: 'time_off', label: 'Has time off this week' },
  { code: 'none', label: 'No availability this week' },
];

type Drawer = 'none' | 'edit' | 'timeOff';

/** Availability tab of `/team` (BR-02..BR-06, BR-25, BR-26): week grid plus its two drawers. */
@Component({
  selector: 'app-availability-tab',
  imports: [
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Message,
    Paginator,
    Select,
    Skeleton,
    AvailabilityDrawer,
    TeamTabs,
    TimeOffDrawer,
  ],
  templateUrl: './availability-tab.html',
  styleUrl: './availability-tab.scss',
})
export class AvailabilityTab {
  private readonly team = inject(TeamService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput = new Subject<string>();
  private request: Subscription | null = null;

  readonly canMutate = input(false);
  readonly branches = input<readonly TeamOption[]>([]);
  readonly branchesLoading = input(false);
  readonly membersCount = input<number | null>(null);

  readonly unauthorized = output<void>();
  readonly forbidden = output<void>();

  readonly errorMessage = AVAILABILITY_ERROR_MESSAGE;
  readonly emptyMessage = EMPTY_TECHNICIANS_MESSAGE;
  readonly filteredEmptyMessage = FILTERED_EMPTY_MESSAGE;
  readonly availabilityOptions = AVAILABILITY_OPTIONS;
  readonly skeletonRows = SKELETON_ROWS;
  readonly pageSize = TEAM_PAGE_SIZE;
  readonly legend = [
    { key: 'available', label: 'Available' },
    { key: 'time_off', label: 'Time off' },
    { key: 'unavailable', label: 'Unavailable' },
  ] as const;

  /** `null` = the current week, resolved by the server; navigation then uses the returned week. */
  private readonly requestedWeek = signal<string | null>(null);
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly branchId = signal<string | null>(null);
  readonly availability = signal<AvailabilityFilter>('all');
  readonly page = signal(1);

  readonly data = signal<AvailabilityPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal<ApiError | null>(null);
  readonly drawer = signal<Drawer>('none');

  readonly weekStart = computed(() => this.requestedWeek() ?? this.data()?.weekStart ?? null);
  readonly range = computed(() => {
    const week = this.weekStart();
    return week === null ? '' : weekRangeLabel(week);
  });
  readonly headers = computed(() => {
    const week = this.weekStart();
    return week === null ? [] : weekDates(week).map((date) => dayHeader(date));
  });
  readonly rows = computed<readonly AvailabilityRow[]>(() => this.data()?.items ?? []);
  readonly totalCount = computed(() => this.data()?.totalCount ?? 0);
  readonly first = computed(() => (this.page() - 1) * TEAM_PAGE_SIZE);
  readonly report = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : this.first() + 1;
    return `Showing ${from} – ${Math.min(this.first() + TEAM_PAGE_SIZE, total)} of ${total} technicians`;
  });
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.branchId() !== null ||
      this.availability() !== 'all',
  );
  readonly branchChoices = computed<Choice[]>(() => [
    { code: 'all', label: 'All branches' },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);

  readonly initials = initials;
  readonly cellText = cellText;
  readonly weeklyHours = weeklyHours;

  constructor() {
    this.searchInput
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((value) => value.trim()),
        filter((value) => value !== this.searchTerm()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((value) => {
        this.searchTerm.set(value);
        this.filtersChanged();
      });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
    this.load();
  }

  load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.request = this.team
      .availability({
        weekStart: this.requestedWeek(),
        search: this.searchTerm(),
        branchId: this.branchId(),
        availability: this.availability(),
        page: this.page(),
      })
      .subscribe({
        next: (data) => {
          if (data.items.length === 0 && data.totalCount > 0 && this.page() > 1) {
            this.page.set(Math.max(1, Math.ceil(data.totalCount / TEAM_PAGE_SIZE)));
            this.load();
            return;
          }
          this.loading.set(false);
          this.data.set(data);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          const apiError = isApiError(error) ? error : null;
          if (apiError?.kind === 'unauthorized') {
            this.unauthorized.emit();
          } else if (apiError?.kind === 'forbidden') {
            this.forbidden.emit();
          } else {
            this.error.set(
              apiError ?? { kind: 'unknown', status: 0, message: '', fieldErrors: {} },
            );
          }
        },
      });
  }

  // Week navigation (BR-02)

  shiftWeek(weeks: number): void {
    const current = this.weekStart();
    if (current === null) {
      return;
    }
    this.requestedWeek.set(addDays(current, weeks * 7));
    this.filtersChanged();
  }

  thisWeek(): void {
    this.requestedWeek.set(null);
    this.data.set(null);
    this.filtersChanged();
  }

  // Filters (BR-06)

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  onBranch(code: string): void {
    this.branchId.set(code === 'all' ? null : code);
    this.filtersChanged();
  }

  onAvailability(value: AvailabilityFilter): void {
    this.availability.set(value);
    this.filtersChanged();
  }

  clearFilters(): void {
    this.searchText.set('');
    this.searchTerm.set('');
    this.branchId.set(null);
    this.availability.set('all');
    this.filtersChanged();
  }

  onPageChange(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
    this.load();
  }

  private filtersChanged(): void {
    this.page.set(1);
    this.load();
  }

  // Drawers (BR-08, BR-13)

  open(drawer: Drawer): void {
    this.drawer.set(drawer);
  }

  closeDrawer(): void {
    this.drawer.set('none');
  }
}
