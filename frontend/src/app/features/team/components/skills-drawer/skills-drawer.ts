import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Subject, Subscription, debounceTime } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { CatalogSkill } from '../../models/skills-availability.model';
import { SkillAssignments } from '../../models/team-planning.model';
import { TeamOption } from '../../models/team.model';
import { SkillsAvailabilityService } from '../../services/skills-availability.service';
import { TeamService } from '../../services/team.service';
import {
  STALE_MESSAGE,
  UNAVAILABLE_TECHNICIAN_MESSAGE,
  fieldMessages,
} from '../../utils/team-planning';
import { TechnicianPicker } from '../technician-picker/technician-picker';

const SEARCH_DEBOUNCE_MS = 300;
const TITLE_ID = 'skills-drawer-title';
const byName = (a: { name: string }, b: { name: string }): number =>
  a.name.localeCompare(b.name, 'en', { sensitivity: 'base' });

type SelectionFilter = 'all' | 'selected' | 'unselected';

/**
 * "Edit skills" drawer (BR-20, BR-21, BR-25): technician select, debounced search over the
 * active catalog, multi-select checklist, optimistic concurrency with Reload, dirty/discard.
 */
@Component({
  selector: 'app-skills-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Message,
    Select,
    Skeleton,
    SpinnerIcon,
    DrawerShell,
    TechnicianPicker,
  ],
  templateUrl: './skills-drawer.html',
  styleUrl: './skills-drawer.scss',
})
export class SkillsDrawer {
  private readonly team = inject(TeamService);
  private readonly catalogService = inject(SkillsAvailabilityService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searches = new Subject<string>();
  private loadRequest: Subscription | null = null;
  private catalogRequest: Subscription | null = null;
  private saveRequest: Subscription | null = null;

  readonly open = input.required<boolean>();
  /** Technician preselected from a row action. */
  readonly initialTechnicianId = input<string | null>(null);

  readonly closed = output<void>();
  readonly saved = output<void>();
  readonly unauthorized = output<void>();

  readonly titleId = TITLE_ID;
  readonly staleMessage = STALE_MESSAGE;
  readonly unavailableMessage = UNAVAILABLE_TECHNICIAN_MESSAGE;

  readonly selection = signal<string | null>(null);
  readonly technicianId = signal<string | null>(null);
  readonly assignments = signal<SkillAssignments | null>(null);
  readonly catalog = signal<readonly CatalogSkill[]>([]);
  readonly catalogLoading = signal(false);
  readonly catalogFailed = signal(false);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly notFound = signal(false);
  readonly submitting = signal(false);
  readonly stale = signal(false);
  readonly saveFailed = signal(false);
  readonly skillsError = signal<string | null>(null);
  readonly selected = signal<readonly string[]>([]);
  readonly searchText = signal('');
  readonly filter = signal<SelectionFilter>('all');
  readonly filterOptions: { code: SelectionFilter; label: string }[] = [
    { code: 'all', label: 'All skills' },
    { code: 'selected', label: 'Selected' },
    { code: 'unselected', label: 'Not selected' },
  ];
  private readonly searchTerm = signal('');
  private readonly snapshot = signal<string | null>(null);

  readonly current = computed<TeamOption | null>(() => {
    const assignments = this.assignments();
    return assignments === null
      ? null
      : {
          id: assignments.technicianId,
          name: `${assignments.firstName} ${assignments.lastName}`,
        };
  });
  readonly activeSkills = computed(() => this.catalog().filter((skill) => skill.isActive));
  readonly visibleSkills = computed(() => {
    const term = this.searchTerm().toLowerCase();
    const filter = this.filter();
    const chosen = this.selectedSet();
    return this.activeSkills().filter(
      (skill) =>
        skill.name.toLowerCase().includes(term) &&
        (filter === 'all' || chosen.has(skill.id) === (filter === 'selected')),
    );
  });
  readonly selectedSet = computed(() => new Set(this.selected()));
  readonly selectedSkills = computed(() =>
    this.activeSkills().filter((skill) => this.selectedSet().has(skill.id)),
  );
  readonly selectedCount = computed(() => this.selected().length);
  readonly dirty = computed(
    () =>
      this.snapshot() !== null && this.snapshot() !== JSON.stringify([...this.selected()].sort()),
  );
  readonly busy = computed(() => this.submitting() || this.loading());
  readonly canSave = computed(() => this.assignments() !== null && this.dirty() && !this.busy());

  constructor() {
    effect(() => {
      if (this.open()) {
        const initial = this.initialTechnicianId();
        untracked(() => this.reset(initial));
      }
    });
    this.searches
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => this.searchTerm.set(value.trim()));
    this.destroyRef.onDestroy(() => {
      this.loadRequest?.unsubscribe();
      this.catalogRequest?.unsubscribe();
      this.saveRequest?.unsubscribe();
    });
  }

  private reset(initial: string | null): void {
    this.loadRequest?.unsubscribe();
    this.selection.set(null);
    this.technicianId.set(null);
    this.assignments.set(null);
    this.selected.set([]);
    this.snapshot.set(null);
    this.searchText.set('');
    this.searchTerm.set('');
    this.filter.set('all');
    this.loading.set(false);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.submitting.set(false);
    this.clearMessages();
    this.loadCatalog();
    if (initial !== null) {
      this.load(initial);
    }
  }

  private clearMessages(): void {
    this.stale.set(false);
    this.saveFailed.set(false);
    this.skillsError.set(null);
  }

  loadCatalog(): void {
    this.catalogRequest?.unsubscribe();
    this.catalogLoading.set(true);
    this.catalogFailed.set(false);
    this.catalogRequest = this.catalogService.catalog().subscribe({
      next: (skills) => {
        this.catalogLoading.set(false);
        this.catalog.set([...skills].sort(byName));
      },
      error: (error: unknown) => {
        this.catalogLoading.set(false);
        if (isApiError(error) && error.kind === 'unauthorized') {
          this.unauthorized.emit();
        } else {
          this.catalogFailed.set(true);
        }
      },
    });
  }

  onSearch(value: string): void {
    this.searchText.set(value);
    this.searches.next(value);
  }

  onPick(id: string): void {
    if (id === this.technicianId() || this.submitting()) {
      this.selection.set(this.technicianId());
      return;
    }
    this.selection.set(id);
    if (!this.dirty()) {
      this.load(id);
      return;
    }
    this.confirmation.confirm(
      discardChangesConfirmation({
        subject: 'these skills',
        accept: () => this.load(id),
        reject: () => this.selection.set(this.technicianId()),
      }),
    );
  }

  load(id: string | null = this.technicianId()): void {
    if (id === null) {
      return;
    }
    this.loadRequest?.unsubscribe();
    this.technicianId.set(id);
    this.selection.set(id);
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    this.clearMessages();
    this.snapshot.set(null);
    this.loadRequest = this.team.skillAssignments(id).subscribe({
      next: (assignments) => {
        this.loading.set(false);
        this.apply(assignments);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.assignments.set(null);
        this.selected.set([]);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (kind === 'not-found') {
          this.notFound.set(true);
        } else {
          this.loadFailed.set(true);
        }
      },
    });
  }

  private apply(assignments: SkillAssignments): void {
    this.assignments.set(assignments);
    this.selected.set([...assignments.skillIds]);
    this.snapshot.set(JSON.stringify([...assignments.skillIds].sort()));
  }

  toggle(skillId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((current) => [
      ...current.filter((id) => id !== skillId),
      ...(checked ? [skillId] : []),
    ]);
    this.skillsError.set(null);
  }

  remove(skillId: string): void {
    if (this.submitting()) {
      return;
    }
    this.selected.update((current) => current.filter((id) => id !== skillId));
    this.skillsError.set(null);
  }

  submit(): void {
    const assignments = this.assignments();
    if (!this.canSave() || assignments === null) {
      return;
    }
    this.clearMessages();
    this.submitting.set(true);
    this.saveRequest = this.team
      .saveSkillAssignments(assignments.technicianId, {
        updatedAt: assignments.updatedAt,
        skillIds: [...this.selected()].sort(),
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.messages.add({ severity: 'success', summary: 'Skills saved.' });
          this.saved.emit();
          this.closed.emit();
        },
        error: (error: unknown) => this.handleFailed(error),
      });
  }

  private handleFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
    } else if (apiError?.kind === 'conflict') {
      this.stale.set(true);
    } else if (apiError?.kind === 'not-found') {
      this.assignments.set(null);
      this.notFound.set(true);
    } else if (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') {
      const message = fieldMessages(apiError.fieldErrors)['skillids'];
      if (message === undefined) {
        this.saveFailed.set(true);
      } else {
        this.skillsError.set(message);
      }
    } else {
      this.saveFailed.set(true);
    }
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmation.confirm(
        discardChangesConfirmation({
          subject: 'these skills',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }
}
