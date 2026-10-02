import {
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Observable } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { CatalogCategory } from '../../models/catalog.model';
import { CatalogCategoriesService } from '../../services/catalog-categories.service';

export const NO_CATEGORIES_MESSAGE = 'No categories yet. Add one to group your services.';
export const CATEGORIES_LOAD_ERROR_MESSAGE =
  "We couldn't load categories. Check your connection and try again.";
export const NAME_REQUIRED_MESSAGE = 'Enter a category name.';
export const NAME_LENGTH_MESSAGE = 'Category name must be 120 characters or fewer.';
export const DUPLICATE_CATEGORY_MESSAGE = 'A category with this name already exists.';
export const SAVE_FAILED_MESSAGE = "We couldn't save the category. Try again.";
const MAX_NAME_LENGTH = 120;

/** BR-11 "Manage categories" dialog (CatalogManage roles): list, create, inline rename, (de)activate. */
@Component({
  selector: 'app-category-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Message, Skeleton, SpinnerIcon, Tag],
  templateUrl: './category-dialog.html',
  styleUrl: './category-dialog.scss',
})
export class CategoryDialog {
  private readonly service = inject(CatalogCategoriesService);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  readonly visible = model(false);
  /** The page owns the loaded list; the dialog reports every change back. */
  readonly categories = input<readonly CatalogCategory[]>([]);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly retry = output<void>();
  readonly categoriesChange = output<readonly CatalogCategory[]>();
  /** A change that can alter the readiness indicator. */
  readonly changed = output<void>();
  readonly unauthorized = output<void>();

  protected readonly emptyMessage = NO_CATEGORIES_MESSAGE;
  protected readonly loadErrorMessage = CATEGORIES_LOAD_ERROR_MESSAGE;
  protected readonly skeletons = [0, 1, 2];

  protected readonly newName = signal('');
  protected readonly createError = signal<string | null>(null);
  protected readonly creating = signal(false);

  protected readonly renamingId = signal<string | null>(null);
  protected readonly renameValue = signal('');
  protected readonly renameError = signal<string | null>(null);
  protected readonly renaming = signal(false);

  /** Row whose activate/deactivate request is in flight. */
  protected readonly toggleId = signal<string | null>(null);

  protected counts(category: CatalogCategory): string {
    const items = category.itemCount === 1 ? 'item' : 'items';
    const services = category.activeServiceCount === 1 ? 'service' : 'services';
    return `${category.itemCount} ${items} · ${category.activeServiceCount} active ${services}`;
  }

  // Create

  protected onNewName(value: string): void {
    this.newName.set(value);
    this.createError.set(null);
  }

  protected create(): void {
    if (this.creating()) {
      return;
    }
    const name = this.newName().trim();
    const invalid = this.validate(name);
    if (invalid !== null) {
      this.createError.set(invalid);
      return;
    }
    this.creating.set(true);
    this.createError.set(null);
    this.service
      .create(name)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.creating.set(false);
          this.newName.set('');
          this.messages.add({ severity: 'success', summary: 'Category added' });
          this.publish([...this.categories(), created]);
        },
        error: (error: unknown) => {
          this.creating.set(false);
          this.createError.set(this.saveError(error));
        },
      });
  }

  // Rename

  protected startRename(category: CatalogCategory): void {
    this.renamingId.set(category.id);
    this.renameValue.set(category.name);
    this.renameError.set(null);
    this.focusLater('category-rename-input');
  }

  protected onRenameValue(value: string): void {
    this.renameValue.set(value);
    this.renameError.set(null);
  }

  protected cancelRename(event?: Event): void {
    event?.stopPropagation();
    const id = this.renamingId();
    if (id === null || this.renaming()) {
      return;
    }
    this.renamingId.set(null);
    this.renameError.set(null);
    this.focusLater(`category-rename-${id}`);
  }

  protected saveRename(): void {
    const id = this.renamingId();
    if (id === null || this.renaming()) {
      return;
    }
    const name = this.renameValue().trim();
    const invalid = this.validate(name);
    if (invalid !== null) {
      this.renameError.set(invalid);
      return;
    }
    this.renaming.set(true);
    this.renameError.set(null);
    this.service
      .rename(id, name)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.renaming.set(false);
          this.renamingId.set(null);
          this.messages.add({ severity: 'success', summary: 'Category renamed' });
          this.publish(this.categories().map((item) => (item.id === saved.id ? saved : item)));
          this.focusLater(`category-rename-${id}`);
        },
        error: (error: unknown) => {
          this.renaming.set(false);
          this.renameError.set(this.saveError(error));
        },
      });
  }

  // Deactivate / reactivate

  protected confirmDeactivate(category: CatalogCategory): void {
    this.confirmation.confirm({
      header: 'Deactivate category',
      message: `Deactivate ${category.name}? Its services will no longer appear in the public request form. Catalog items will not be deleted.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Deactivate', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.toggle(category, false),
    });
  }

  protected reactivate(category: CatalogCategory): void {
    this.toggle(category, true);
  }

  private toggle(category: CatalogCategory, active: boolean): void {
    if (this.toggleId() !== null) {
      return;
    }
    this.toggleId.set(category.id);
    const request$: Observable<void> = active
      ? this.service.activate(category.id)
      : this.service.deactivate(category.id);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.toggleId.set(null);
        this.messages.add({
          severity: 'success',
          summary: `${category.name} ${active ? 'reactivated' : 'deactivated'}`,
        });
        this.publish(
          this.categories().map((item) =>
            item.id === category.id ? { ...item, isActive: active } : item,
          ),
        );
      },
      error: (error: unknown) => {
        this.toggleId.set(null);
        if (isApiError(error) && error.kind === 'unauthorized') {
          this.unauthorized.emit();
          return;
        }
        this.messages.add({
          severity: 'error',
          summary: `We couldn't update ${category.name}. Try again.`,
        });
      },
    });
  }

  // Shared

  private validate(name: string): string | null {
    if (name.length === 0) {
      return NAME_REQUIRED_MESSAGE;
    }
    return name.length > MAX_NAME_LENGTH ? NAME_LENGTH_MESSAGE : null;
  }

  private saveError(error: unknown): string | null {
    if (!isApiError(error)) {
      return SAVE_FAILED_MESSAGE;
    }
    switch (error.kind) {
      case 'unauthorized':
        this.unauthorized.emit();
        return null;
      case 'conflict':
        return DUPLICATE_CATEGORY_MESSAGE;
      case 'validation':
      case 'bad-request':
        return error.fieldErrors['name']?.[0] ?? SAVE_FAILED_MESSAGE;
      default:
        return SAVE_FAILED_MESSAGE;
    }
  }

  private publish(list: readonly CatalogCategory[]): void {
    const sorted = [...list].sort(
      (a, b) =>
        a.name.localeCompare(b.name, 'en', { sensitivity: 'base' }) || a.id.localeCompare(b.id),
    );
    this.categoriesChange.emit(sorted);
    this.changed.emit();
  }

  private focusLater(id: string): void {
    afterNextRender(() => document.getElementById(id)?.focus(), { injector: this.injector });
  }
}
