import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { CatalogSkill } from '../../models/skills-availability.model';
import { SkillsAvailabilityService } from '../../services/skills-availability.service';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
export const NO_CATALOG_MESSAGE = (): string => t('team.skillCatalogDialog.noCatalogMessage');
const NEW_ID = '';

interface Draft {
  /** `NEW_ID` for a skill not yet created. */
  readonly id: string;
  readonly name: string;
  readonly description: string;
}

type StatusFilter = 'all' | 'active' | 'inactive';

/** BR-17 "Manage skills" dialog (editors): search, add/edit panel, list, activate/deactivate. */
@Component({
  selector: 'app-skill-catalog-dialog',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    Dialog,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Message,
    Select,
    Skeleton,
    Tag,
  ],
  templateUrl: './skill-catalog-dialog.html',
  styleUrl: './skill-catalog-dialog.scss',
})
export class SkillCatalogDialog {
  private readonly service = inject(SkillsAvailabilityService);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model(false);
  /** Emits the latest catalog after a load or change, so the picker refreshes. */
  readonly catalogChange = output<readonly CatalogSkill[]>();
  readonly unauthorized = output<void>();

  private readonly menu = viewChild.required(Menu);

  protected get emptyMessage(): string {
    return NO_CATALOG_MESSAGE();
  }
  protected readonly skeletons = [0, 1, 2];
  protected readonly statusOptions: { code: StatusFilter; label: string }[] = localizedList(() => [
    { code: 'all', label: t('team.skillCatalogDialog.allSkills') },
    { code: 'active', label: t('common.active') },
    { code: 'inactive', label: t('common.inactive') },
  ]);
  protected readonly searchText = signal('');
  protected readonly statusFilter = signal<StatusFilter>('all');
  protected readonly menuModel = signal<MenuItem[]>([]);
  protected readonly skills = signal<readonly CatalogSkill[]>([]);
  protected readonly visibleSkills = computed(() => {
    const term = this.searchText().trim().toLowerCase();
    const status = this.statusFilter();
    return this.skills().filter(
      (skill) =>
        (status === 'all' || skill.isActive === (status === 'active')) &&
        (term === '' ||
          skill.name.toLowerCase().includes(term) ||
          (skill.description ?? '').toLowerCase().includes(term)),
    );
  });
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly draft = signal<Draft | null>(null);
  protected readonly nameError = signal<string | null>(null);
  protected readonly descriptionError = signal<string | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  readonly dirty = computed(() => {
    const draft = this.draft();
    if (draft === null) {
      return false;
    }
    const original = this.skills().find((skill) => skill.id === draft.id);
    return (
      draft.name !== (original?.name ?? '') || draft.description !== (original?.description ?? '')
    );
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => {
          this.draft.set(null);
          this.searchText.set('');
          this.statusFilter.set('all');
          this.load();
        });
      }
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .catalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (skills) => this.apply(skills),
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.checkUnauthorized(error);
        },
      });
  }

  private apply(skills: readonly CatalogSkill[]): void {
    const sorted = [...skills].sort((a, b) =>
      a.name.localeCompare(b.name, 'en', { sensitivity: 'base' }),
    );
    this.skills.set(sorted);
    this.loading.set(false);
    this.catalogChange.emit(sorted);
  }

  private checkUnauthorized(error: unknown): void {
    if (isApiError(error) && error.kind === 'unauthorized') {
      this.unauthorized.emit();
    }
  }

  protected startAdd(): void {
    this.clearErrors();
    this.draft.set({ id: NEW_ID, name: '', description: '' });
  }

  protected startEdit(skill: CatalogSkill): void {
    this.clearErrors();
    this.draft.set({ id: skill.id, name: skill.name, description: skill.description ?? '' });
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => (draft ? { ...draft, ...change } : draft));
    this.nameError.set(null);
    this.descriptionError.set(null);
  }

  protected cancelEdit(): void {
    this.draft.set(null);
    this.clearErrors();
  }

  private clearErrors(): void {
    this.nameError.set(null);
    this.descriptionError.set(null);
    this.failed.set(false);
  }

  protected save(): void {
    const draft = this.draft();
    if (draft === null || this.busy()) {
      return;
    }
    const name = draft.name.trim();
    const description = draft.description.trim();
    this.clearErrors();
    if (name === '') {
      this.nameError.set(t('team.skillCatalogDialog.enterASkillName'));
    } else if (name.length > 120) {
      this.nameError.set(t('team.skillCatalogDialog.use120CharactersOr'));
    } else if (
      this.skills().some(
        (skill) => skill.id !== draft.id && skill.name.toLowerCase() === name.toLowerCase(),
      )
    ) {
      this.nameError.set(t('team.skillCatalogDialog.aSkillWithThis'));
    }
    if (description.length > 500) {
      this.descriptionError.set(t('team.skillCatalogDialog.use500CharactersOr'));
    }
    if (this.nameError() !== null || this.descriptionError() !== null) {
      return;
    }
    const body = { name, ...(description === '' ? {} : { description }) };
    const request =
      draft.id === NEW_ID
        ? this.service.createSkill(body)
        : this.service.updateSkill(draft.id, body);
    this.busy.set(true);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.draft.set(null);
        this.messages.add({
          severity: 'success',
          summary: t('team.skillCatalogDialog.skillSaved'),
        });
        this.apply(
          draft.id === NEW_ID
            ? [...this.skills(), saved]
            : this.skills().map((skill) => (skill.id === saved.id ? saved : skill)),
        );
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.checkUnauthorized(error);
        if (isApiError(error) && error.kind === 'validation') {
          this.nameError.set(error.fieldErrors['name']?.[0] ?? null);
          this.descriptionError.set(error.fieldErrors['description']?.[0] ?? null);
        } else if (isApiError(error) && error.kind === 'not-found') {
          this.draft.set(null);
          this.load();
        } else if (!isApiError(error) || error.kind !== 'unauthorized') {
          this.failed.set(true);
        }
      },
    });
  }

  protected setActive(skill: CatalogSkill, active: boolean): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.failed.set(false);
    this.service
      .setSkillActive(skill.id, active)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.messages.add({
            severity: 'success',
            summary: t('team.skillCatalogDialog.skillSaved'),
          });
          this.apply(
            this.skills().map((item) =>
              item.id === skill.id ? { ...item, isActive: active } : item,
            ),
          );
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.failed.set(true);
          this.checkUnauthorized(error);
        },
      });
  }

  protected openMenu(event: Event, skill: CatalogSkill): void {
    this.menuModel.set([
      {
        label: skill.isActive
          ? t('team.skillCatalogDialog.deactivate')
          : t('team.skillCatalogDialog.activate'),
        icon: skill.isActive ? 'pi pi-ban' : 'pi pi-check-circle',
        command: () => this.setActive(skill, !skill.isActive),
      },
    ]);
    this.menu().toggle(event);
  }

  protected onVisibleChange(open: boolean): void {
    if (open) {
      this.visible.set(true);
    } else {
      this.close();
    }
  }

  close(): void {
    if (this.busy()) {
      return;
    }
    if (!this.dirty()) {
      this.visible.set(false);
      return;
    }
    this.confirmation.confirm(
      discardChangesConfirmation({
        subject: t('team.skillCatalogDialog.thisSkill'),
        accept: () => this.visible.set(false),
      }),
    );
  }
}
