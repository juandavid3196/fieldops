import {
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MenuItem } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Subject, Subscription, debounceTime, filter, map } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { MatrixRow, SkillCoverageFilter, SkillsMatrixPage } from '../../models/team-planning.model';
import { TEAM_PAGE_SIZE, TeamOption } from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import { initials } from '../../utils/team-planning';
import { SkillCatalogDialog } from '../skill-catalog-dialog/skill-catalog-dialog';
import { SkillsDrawer } from '../skills-drawer/skills-drawer';
import { TeamTabs } from '../team-tabs/team-tabs';

export const SKILLS_ERROR_MESSAGE = "We couldn't load skills. Try again.";
export const SKILLS_NOTE = 'Skills help Dispatch match technicians to the right jobs.';
export const NO_SKILLS_MESSAGE = 'No skills defined yet.';
const EMPTY_TECHNICIANS_MESSAGE = 'No active technicians yet.';
const FILTERED_EMPTY_MESSAGE = 'No technicians match these filters.';
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];

interface Choice {
  readonly code: string;
  readonly label: string;
}

const COVERAGE_OPTIONS: Choice[] = [
  { code: 'all', label: 'All coverage' },
  { code: 'with', label: 'With skills' },
  { code: 'without', label: 'Without skills' },
];

interface MatrixRowView {
  readonly row: MatrixRow;
  readonly assigned: ReadonlySet<string>;
  readonly name: string;
}

/** Skills tab of `/team` (BR-17..BR-20, BR-25, BR-26): technician × skill matrix and its drawer. */
@Component({
  selector: 'app-skills-tab',
  imports: [
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Message,
    Paginator,
    Select,
    Skeleton,
    SkillCatalogDialog,
    SkillsDrawer,
    TeamTabs,
  ],
  templateUrl: './skills-tab.html',
  styleUrl: './skills-tab.scss',
})
export class SkillsTab {
  private readonly team = inject(TeamService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput = new Subject<string>();
  private readonly menu = viewChild.required(Menu);
  private request: Subscription | null = null;

  readonly canMutate = input(false);
  readonly branches = input<readonly TeamOption[]>([]);
  readonly branchesLoading = input(false);
  readonly membersCount = input<number | null>(null);

  readonly unauthorized = output<void>();
  readonly forbidden = output<void>();

  readonly errorMessage = SKILLS_ERROR_MESSAGE;
  readonly note = SKILLS_NOTE;
  readonly noSkillsMessage = NO_SKILLS_MESSAGE;
  readonly emptyMessage = EMPTY_TECHNICIANS_MESSAGE;
  readonly filteredEmptyMessage = FILTERED_EMPTY_MESSAGE;
  readonly coverageOptions = COVERAGE_OPTIONS;
  readonly skeletonRows = SKELETON_ROWS;
  readonly pageSize = TEAM_PAGE_SIZE;
  readonly initials = initials;

  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly branchId = signal<string | null>(null);
  readonly skillId = signal<string | null>(null);
  readonly coverage = signal<SkillCoverageFilter>('all');
  readonly sort = signal<'asc' | 'desc'>('asc');
  readonly page = signal(1);

  readonly data = signal<SkillsMatrixPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal<ApiError | null>(null);
  readonly drawerOpen = signal(false);
  readonly drawerTechnicianId = signal<string | null>(null);
  readonly catalogOpen = signal(false);
  readonly menuModel = signal<MenuItem[]>([]);

  readonly skills = computed(() => this.data()?.skills ?? []);
  readonly views = computed<readonly MatrixRowView[]>(() =>
    (this.data()?.items ?? []).map((row) => ({
      row,
      assigned: new Set(row.skillIds),
      name: `${row.firstName} ${row.lastName}`,
    })),
  );
  readonly totalCount = computed(() => this.data()?.totalCount ?? 0);
  readonly first = computed(() => (this.page() - 1) * TEAM_PAGE_SIZE);
  readonly report = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : this.first() + 1;
    return `Showing ${from} – ${Math.min(this.first() + TEAM_PAGE_SIZE, total)} of ${total} technicians`;
  });
  readonly ariaSort = computed(() => (this.sort() === 'asc' ? 'ascending' : 'descending'));
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.branchId() !== null ||
      this.skillId() !== null ||
      this.coverage() !== 'all',
  );
  readonly branchChoices = computed<Choice[]>(() => [
    { code: 'all', label: 'All branches' },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  /** Active skills seen so far; kept while a new page loads so the filter stays usable. */
  private readonly knownSkills = signal<readonly TeamOption[]>([]);
  readonly skillChoices = computed<Choice[]>(() => [
    { code: 'all', label: 'All skills' },
    ...this.knownSkills().map((skill) => ({ code: skill.id, label: skill.name })),
  ]);

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
      .skillsMatrix({
        search: this.searchTerm(),
        branchId: this.branchId(),
        skillId: this.skillId(),
        coverage: this.coverage(),
        sort: this.sort(),
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
          this.knownSkills.set(data.skills);
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

  // Filters and sorting (BR-17, BR-18)

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  onBranch(code: string): void {
    this.branchId.set(code === 'all' ? null : code);
    this.filtersChanged();
  }

  onSkill(code: string): void {
    this.skillId.set(code === 'all' ? null : code);
    this.filtersChanged();
  }

  onCoverage(value: SkillCoverageFilter): void {
    this.coverage.set(value);
    this.filtersChanged();
  }

  toggleSort(): void {
    this.sort.set(this.sort() === 'asc' ? 'desc' : 'asc');
    this.filtersChanged();
  }

  clearFilters(): void {
    this.searchText.set('');
    this.searchTerm.set('');
    this.branchId.set(null);
    this.skillId.set(null);
    this.coverage.set('all');
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

  // Manage actions (BR-19, BR-20)

  openDrawer(technicianId: string | null): void {
    this.drawerTechnicianId.set(technicianId);
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  openMenu(event: Event, view: MatrixRowView): void {
    this.menuModel.set([
      {
        label: 'Edit skills',
        icon: 'pi pi-pencil',
        command: () => this.openDrawer(view.row.technicianId),
      },
    ]);
    this.menu().toggle(event);
  }

  /** Closing the catalog refreshes the matrix columns. */
  onCatalogVisible(visible: boolean): void {
    this.catalogOpen.set(visible);
    if (!visible) {
      this.load();
    }
  }
}
