import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
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
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Observable, Subscription } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ErrorSummary,
  FieldErrorLink,
} from '../../../organizations/components/error-summary/error-summary';
import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  CatalogDetail,
  CatalogImage,
  CatalogItemRequest,
  CatalogType,
  ITEM_FIELD_KEYS,
  ItemFieldErrors,
  ItemFieldKey,
} from '../../models/catalog.model';
import { CatalogItemsService } from '../../services/catalog-items.service';
import {
  IMAGE_ACCEPT,
  IMAGE_SIZE_MESSAGE,
  IMAGE_TYPE_MESSAGE,
  MoneyFormat,
  marginPercent,
  marginText,
  normalizeName,
  parseMoneyCents,
  usageText,
  validateImageFile,
} from '../../utils/catalog-format';
import {
  DUPLICATE_NAME_MESSAGE,
  FIELD_LABELS,
  ItemFormValue,
  mapServerFieldErrors,
  validateItem,
  validateItemField,
} from './item-drawer.validators';
import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';

export type ItemDrawerMode = 'create' | 'edit' | 'view';

export const ITEM_UNAVAILABLE_MESSAGE = 'This item is no longer available.';
const UNEXPECTED_MESSAGE = 'An unexpected error occurred. Please try again.';
const TITLE_ID = 'item-drawer-title';
const SUMMARY_ID = 'item-drawer-error-summary';
const CONTROL_IDS: Readonly<Record<ItemFieldKey, string>> = {
  type: 'item-type-service',
  name: 'item-name',
  description: 'item-description',
  unitCost: 'item-unit-cost',
  unitPrice: 'item-unit-price',
};

/**
 * Add / Edit / Details drawer (FR-08, FR-11): one component, three modes. Owns validation
 * (BR-07), the live margin (BR-09), the staged image flow (BR-11), server-error mapping and
 * the dirty-close guard; the page owns refreshes, the 401 redirect and the 404 handling.
 */
@Component({
  selector: 'app-item-drawer',
  imports: [
    FormsModule,
    ButtonDirective,
    InputText,
    Message,
    Skeleton,
    SpinnerIcon,
    Textarea,
    ToggleSwitch,
    DrawerShell,
    ErrorSummary,
    FormField,
  ],
  templateUrl: './item-drawer.html',
  styleUrl: './item-drawer.scss',
})
export class ItemDrawer {
  private readonly catalog = inject(CatalogItemsService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  readonly mode = input.required<ItemDrawerMode>();
  /** Id of the item opened in edit/view mode. */
  readonly itemId = input<string | null>(null);
  readonly money = input.required<MoneyFormat>();

  /** The item was saved and the drawer closes. */
  readonly saved = output<void>();
  /** The item was saved but the drawer stays open (post-save image failure): refresh the list. */
  readonly changed = output<void>();
  readonly closed = output<void>();
  readonly unauthorized = output<void>();
  /** The item no longer exists (`404`). */
  readonly unavailable = output<void>();

  readonly titleId = TITLE_ID;
  readonly summaryId = SUMMARY_ID;
  readonly controlIds = CONTROL_IDS;
  readonly imageAccept = IMAGE_ACCEPT;
  readonly typeOptions: readonly { value: CatalogType; label: string }[] = [
    { value: 'service', label: 'Service' },
    { value: 'product', label: 'Product' },
  ];

  /** Effective mode: a create that saved its item but failed its image continues as an edit. */
  private readonly savedId = signal<string | null>(null);
  readonly currentMode = computed<ItemDrawerMode>(() =>
    this.mode() === 'create' && this.savedId() !== null ? 'edit' : this.mode(),
  );
  readonly currentId = computed(() => this.savedId() ?? this.itemId());
  readonly isCreate = computed(() => this.currentMode() === 'create');
  readonly readOnly = computed(() => this.currentMode() === 'view');
  readonly title = computed(() =>
    this.isCreate() ? 'Add item' : this.readOnly() ? 'Item details' : 'Edit item',
  );

  readonly type = signal<CatalogType | null>(null);
  readonly name = signal('');
  readonly description = signal('');
  readonly unitCost = signal('');
  readonly unitPrice = signal('');
  readonly isTaxable = signal(true);
  readonly isActive = signal(true);

  readonly fieldErrors = signal<ItemFieldErrors>({});
  readonly pageMessage = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly detail = signal<CatalogDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailFailed = signal(false);

  // Image: the stored image (from the detail) plus the staged change sent after the item saves.
  readonly existingImage = signal<CatalogImage | null>(null);
  private readonly existingPreview = signal<string | null>(null);
  readonly stagedFile = signal<File | null>(null);
  private readonly stagedPreview = signal<string | null>(null);
  readonly removeRequested = signal(false);
  readonly imageError = signal<string | null>(null);
  readonly dragging = signal(false);

  readonly previewUrl = computed(() => this.stagedPreview() ?? this.existingPreview());
  readonly hasImage = computed(
    () => this.stagedFile() !== null || (this.existingImage() !== null && !this.removeRequested()),
  );

  private readonly snapshot = signal<string | null>(null);
  private submitAttempted = false;
  private detailRequest: Subscription | null = null;

  readonly costCents = computed(() => parseMoneyCents(this.unitCost()));
  readonly priceCents = computed(() => parseMoneyCents(this.unitPrice()));
  readonly margin = computed(() => {
    const cost = this.costCents();
    const price = this.priceCents();
    return cost === null || price === null ? null : marginPercent(cost, price);
  });
  readonly marginText = computed(() => marginText(this.margin()));
  readonly profitText = computed(() => {
    const cost = this.costCents();
    const price = this.priceCents();
    return cost === null || price === null ? '—' : this.money().format((price - cost) / 100);
  });
  readonly usage = computed(() => {
    const detail = this.detail();
    return detail === null ? null : usageText(detail.usage);
  });
  readonly currencyLabel = computed(() => {
    const code = this.money().currency;
    return code === null ? '' : ` in ${code}`;
  });

  readonly errorLinks = computed<readonly FieldErrorLink[]>(() =>
    ITEM_FIELD_KEYS.filter((field) => this.fieldErrors()[field] !== undefined).map((field) => ({
      fieldId: CONTROL_IDS[field],
      label: FIELD_LABELS[field],
      message: this.fieldErrors()[field] as string,
    })),
  );

  private readonly currentKey = computed(() =>
    JSON.stringify([
      this.type(),
      this.name(),
      this.description(),
      this.unitCost(),
      this.unitPrice(),
      this.isTaxable(),
      this.isActive(),
      this.stagedFile()?.name ?? null,
      this.stagedFile()?.size ?? null,
      this.removeRequested(),
    ]),
  );
  readonly dirty = computed(
    () => this.snapshot() !== null && this.snapshot() !== this.currentKey(),
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        const mode = this.mode();
        const id = this.itemId();
        untracked(() => this.initialize(mode, id));
      }
    });
    this.destroyRef.onDestroy(() => {
      this.detailRequest?.unsubscribe();
      this.setStagedPreview(null);
      this.setExistingPreview(null);
    });
  }

  error(field: ItemFieldKey): string | null {
    return this.fieldErrors()[field] ?? null;
  }

  private initialize(mode: ItemDrawerMode, id: string | null): void {
    this.detailRequest?.unsubscribe();
    this.savedId.set(null);
    this.submitAttempted = false;
    this.submitting.set(false);
    this.fieldErrors.set({});
    this.pageMessage.set(null);
    this.imageError.set(null);
    this.detail.set(null);
    this.detailFailed.set(false);
    this.stagedFile.set(null);
    this.setStagedPreview(null);
    this.removeRequested.set(false);
    this.existingImage.set(null);
    this.setExistingPreview(null);
    this.resetFields(null);

    if (mode === 'create' || id === null) {
      this.detailLoading.set(false);
      this.snapshot.set(this.currentKey());
      return;
    }
    this.loadDetail(id);
  }

  loadDetail(id: string | null = this.currentId()): void {
    if (id === null) {
      return;
    }
    this.detailRequest?.unsubscribe();
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.snapshot.set(null);
    this.detailRequest = this.catalog.get(id).subscribe({
      next: (detail) => {
        this.detailLoading.set(false);
        this.detail.set(detail);
        this.resetFields(detail);
        this.existingImage.set(detail.image);
        this.snapshot.set(this.currentKey());
        if (detail.image !== null) {
          this.loadPreview(detail.id);
        }
      },
      error: (error: unknown) => {
        this.detailLoading.set(false);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (apiError?.kind === 'not-found') {
          this.unavailable.emit();
        } else {
          this.detailFailed.set(true);
        }
      },
    });
  }

  private loadPreview(id: string): void {
    this.catalog
      .getImage(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => this.setExistingPreview(URL.createObjectURL(blob)),
        error: () => this.setExistingPreview(null),
      });
  }

  private resetFields(detail: CatalogDetail | null): void {
    this.type.set(detail?.type ?? null);
    this.name.set(detail?.name ?? '');
    this.description.set(detail?.description ?? '');
    this.unitCost.set(detail === null ? '' : detail.unitCost.toFixed(2));
    this.unitPrice.set(detail === null ? '' : detail.unitPrice.toFixed(2));
    this.isTaxable.set(detail?.isTaxable ?? true);
    this.isActive.set(detail?.isActive ?? true);
  }

  // Field handling

  onTypeChange(value: CatalogType): void {
    this.type.set(value);
    this.revalidate('type');
  }

  onText(field: 'name' | 'description' | 'unitCost' | 'unitPrice', value: string): void {
    this[field].set(value);
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  onFieldBlur(field: ItemFieldKey): void {
    if (this.submitting() || this.readOnly()) {
      return;
    }
    this.setFieldError(field, this.validate(field));
  }

  private revalidate(field: ItemFieldKey): void {
    if (this.fieldErrors()[field] !== undefined || this.submitAttempted) {
      this.setFieldError(field, this.validate(field));
    }
  }

  private formValue(): ItemFormValue {
    return {
      type: this.type(),
      name: this.name(),
      description: this.description(),
      unitCost: this.unitCost(),
      unitPrice: this.unitPrice(),
    };
  }

  private validate(field: ItemFieldKey): string | null {
    return validateItemField(field, this.formValue());
  }

  private setFieldError(field: ItemFieldKey, message: string | null): void {
    this.fieldErrors.update((errors) => {
      const next = { ...errors };
      if (message === null) {
        delete next[field];
      } else {
        next[field] = message;
      }
      return next;
    });
  }

  // Image staging (BR-11)

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    input.value = '';
    this.stage(file);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (!this.submitting() && !this.readOnly()) {
      this.stage(event.dataTransfer?.files.item(0) ?? null);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(!this.submitting() && !this.readOnly());
  }

  private stage(file: File | null): void {
    if (file === null || this.submitting() || this.readOnly()) {
      return;
    }
    const message = validateImageFile(file);
    if (message !== null) {
      this.imageError.set(message);
      return;
    }
    this.imageError.set(null);
    this.stagedFile.set(file);
    this.removeRequested.set(false);
    this.setStagedPreview(URL.createObjectURL(file));
  }

  removeImage(): void {
    if (this.submitting()) {
      return;
    }
    this.imageError.set(null);
    this.stagedFile.set(null);
    this.setStagedPreview(null);
    this.removeRequested.set(this.existingImage() !== null);
  }

  private setStagedPreview(url: string | null): void {
    const previous = this.stagedPreview();
    if (previous !== null) {
      URL.revokeObjectURL(previous);
    }
    this.stagedPreview.set(url);
  }

  private setExistingPreview(url: string | null): void {
    const previous = this.existingPreview();
    if (previous !== null) {
      URL.revokeObjectURL(previous);
    }
    this.existingPreview.set(url);
  }

  // Submit

  submit(): void {
    if (this.submitting() || this.readOnly() || this.detailLoading() || this.detailFailed()) {
      return;
    }
    this.submitAttempted = true;
    this.pageMessage.set(null);
    this.imageError.set(null);

    const errors = validateItem(this.formValue());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusFirstInvalid();
      return;
    }

    const body: CatalogItemRequest = {
      type: this.type() as CatalogType,
      name: normalizeName(this.name()),
      description: this.description().trim() === '' ? null : this.description().trim(),
      unitCost: (this.costCents() as number) / 100,
      unitPrice: (this.priceCents() as number) / 100,
      isTaxable: this.isTaxable(),
      isActive: this.isActive(),
    };
    const id = this.currentId();
    const request$ = id === null ? this.catalog.create(body) : this.catalog.update(id, body);
    const created = id === null;

    this.submitting.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => this.handleItemSaved(saved, created),
      error: (error: unknown) => this.handleItemFailed(error),
    });
  }

  private handleItemSaved(saved: CatalogDetail, created: boolean): void {
    this.savedId.set(saved.id);
    this.detail.set(saved);
    const staged = this.stagedFile();
    let image$: Observable<unknown> | null = null;
    if (staged !== null) {
      image$ = this.catalog.putImage(saved.id, staged);
    } else if (this.removeRequested() && this.existingImage() !== null) {
      image$ = this.catalog.deleteImage(saved.id);
    }

    if (image$ === null) {
      this.finish(created);
      return;
    }
    image$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.finish(created),
      error: (error: unknown) => this.handleImageFailed(error, saved),
    });
  }

  private finish(created: boolean): void {
    this.submitting.set(false);
    this.messageService.add({
      severity: 'success',
      summary: created ? 'Item added' : 'Item updated',
    });
    this.closed.emit();
    this.saved.emit();
  }

  /** The item is saved: keep the drawer open in edit mode, refresh the list and show the image error. */
  private handleImageFailed(error: unknown, saved: CatalogDetail): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    this.imageError.set(this.imageMessage(apiError));
    this.resetFieldsKeepImage(saved);
    this.changed.emit();
    this.focusSummary();
  }

  /** The saved values become the clean baseline; the failed image stays staged. */
  private resetFieldsKeepImage(saved: CatalogDetail): void {
    this.unitCost.set(saved.unitCost.toFixed(2));
    this.unitPrice.set(saved.unitPrice.toFixed(2));
    this.name.set(saved.name);
    this.description.set(saved.description ?? '');
    this.snapshot.set(this.currentKey());
  }

  private imageMessage(apiError: ApiError | null): string {
    const fileMessage = apiError?.fieldErrors['file']?.[0];
    if (apiError?.status === 413) {
      return IMAGE_SIZE_MESSAGE;
    }
    if (apiError?.status === 415) {
      return IMAGE_TYPE_MESSAGE;
    }
    return fileMessage ?? apiError?.message ?? UNEXPECTED_MESSAGE;
  }

  private handleItemFailed(error: unknown): void {
    this.submitting.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;

    switch (apiError?.kind) {
      case 'unauthorized':
        this.unauthorized.emit();
        return;
      case 'not-found':
        this.unavailable.emit();
        return;
      case 'conflict':
        this.fieldErrors.set({ name: DUPLICATE_NAME_MESSAGE });
        this.focusFirstInvalid();
        return;
      case 'validation':
      case 'bad-request': {
        const mapped = mapServerFieldErrors(apiError.fieldErrors);
        this.fieldErrors.set(mapped);
        if (Object.keys(mapped).length > 0) {
          this.focusFirstInvalid();
        } else {
          this.pageMessage.set(apiError.message);
          this.focusSummary();
        }
        return;
      }
    }
    this.pageMessage.set(apiError?.message ?? UNEXPECTED_MESSAGE);
    this.focusSummary();
  }

  // Closing

  /** Read by the page's route-leave guard and drawer switching. */
  isDirty(): boolean {
    return this.open() && this.dirty();
  }

  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (this.dirty()) {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this item',
          accept: () => this.closed.emit(),
        }),
      );
      return;
    }
    this.closed.emit();
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const target = this.hostElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        if (target) {
          target.focus();
        } else {
          this.focusSummaryNow();
        }
      },
      { injector: this.injector },
    );
  }

  private focusSummary(): void {
    afterNextRender(() => this.focusSummaryNow(), { injector: this.injector });
  }

  private focusSummaryNow(): void {
    this.hostElement.querySelector<HTMLElement>(`#${SUMMARY_ID}`)?.focus();
  }
}
