import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  of,
  switchMap,
  tap,
} from 'rxjs';

import {
  CatalogItem,
  NewMaterial,
  TechnicianVisitDetail,
} from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  QUANTITY_INVALID_MESSAGE,
  classifyMutationFailure,
  formatQuantity,
  parseQuantity,
  stepQuantity,
} from '../../utils/job-progress';

const MIN_SEARCH = 2;

interface MaterialRow {
  readonly id: string;
  readonly kind: 'planned' | 'additional';
  readonly description: string;
  readonly unit: string;
  /** Planned quantity; `null` for additional materials. */
  readonly planned: number | null;
  readonly used: number;
}

/** BR-17 Materials section: planned and additional quantities with steppers and Add material. */
@Component({
  selector: 'app-job-materials',
  imports: [ButtonDirective, Dialog],
  templateUrl: './job-materials.html',
  styleUrl: './job-materials.scss',
})
export class JobMaterials {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly search$ = new Subject<string>();
  /** Triggers of the open dialogs; focus returns to them on close. */
  private addTrigger: HTMLElement | null = null;
  private removeTrigger: HTMLElement | null = null;
  private readonly addButton = viewChild<ElementRef<HTMLElement>>('addButton');

  readonly visit = input.required<TechnicianVisitDetail>();
  readonly readOnly = input(false);
  readonly visitChange = output<TechnicianVisitDetail>();
  readonly reload = output<void>();

  readonly pending = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  /** Typed text per row, kept after a failed save. */
  readonly drafts = signal<Readonly<Record<string, string>>>({});
  readonly removeTarget = signal<MaterialRow | null>(null);

  readonly addOpen = signal(false);
  readonly addError = signal<string | null>(null);
  readonly freeText = signal(false);
  readonly searchText = signal('');
  readonly results = signal<readonly CatalogItem[]>([]);
  readonly searching = signal(false);
  readonly searchFailed = signal(false);
  readonly selected = signal<CatalogItem | null>(null);
  readonly description = signal('');
  readonly unit = signal('');
  readonly quantity = signal('');

  readonly rows = computed<MaterialRow[]>(() => {
    const visit = this.visit();
    return [
      ...visit.plannedMaterials.map((material) => ({
        id: material.id,
        kind: 'planned' as const,
        description: material.description,
        unit: material.unit,
        planned: material.quantity,
        used: material.usedQuantity,
      })),
      ...visit.additionalMaterials.map((material) => ({
        id: material.id,
        kind: 'additional' as const,
        description: material.description,
        unit: material.unit,
        planned: null,
        used: material.quantity,
      })),
    ];
  });
  readonly matches = computed(
    () =>
      this.visit().additionalMaterials.length === 0 &&
      this.visit().plannedMaterials.every(
        (material) => material.usedQuantity === material.quantity,
      ),
  );
  readonly canAdd = computed(() => {
    const quantity = parseQuantity(this.quantity());
    const source = this.freeText()
      ? this.description().trim() !== '' && this.unit().trim() !== ''
      : this.selected() !== null;
    return quantity !== null && quantity > 0 && source;
  });

  constructor() {
    this.search$
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        tap(() => this.searchFailed.set(false)),
        switchMap((text) => {
          if (text.length < MIN_SEARCH) {
            this.searching.set(false);
            return of<readonly CatalogItem[]>([]);
          }
          this.searching.set(true);
          return this.visits.searchCatalog(this.visit().visitId, text).pipe(
            catchError(() => {
              this.searchFailed.set(true);
              return of<readonly CatalogItem[]>([]);
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((items) => {
        this.searching.set(false);
        this.results.set(items);
      });
  }

  display(row: MaterialRow): string {
    return this.drafts()[row.id] ?? formatQuantity(row.used);
  }

  typed(row: MaterialRow, text: string): void {
    this.drafts.update((drafts) => ({ ...drafts, [row.id]: text }));
  }

  step(row: MaterialRow, delta: number, trigger: HTMLElement): void {
    this.commit(row, stepQuantity(row.used, delta), trigger);
  }

  /** Typed decimals (≤ 3 places, never below 0) are saved when the field loses focus. */
  commitTyped(row: MaterialRow, text: string, trigger: HTMLElement): void {
    const value = parseQuantity(text);
    if (value === null) {
      this.error.set(QUANTITY_INVALID_MESSAGE);
      return;
    }
    if (value === row.used) {
      this.error.set(null);
      this.clearDraft(row.id);
      return;
    }
    this.commit(row, value, trigger);
  }

  confirmRemove(): void {
    const row = this.removeTarget();
    if (row !== null) {
      this.send(row, 0, () => this.closeRemove());
    }
  }

  closeRemove(): void {
    if (this.removeTarget() !== null) {
      this.removeTarget.set(null);
      this.restoreFocus(this.removeTrigger);
    }
  }

  openAdd(trigger: HTMLElement): void {
    this.addTrigger = trigger;
    this.addError.set(null);
    this.freeText.set(false);
    this.searchText.set('');
    this.results.set([]);
    this.searchFailed.set(false);
    this.selected.set(null);
    this.description.set('');
    this.unit.set('');
    this.quantity.set('');
    this.addOpen.set(true);
  }

  closeAdd(): void {
    if (this.addOpen()) {
      this.addOpen.set(false);
      this.restoreFocus(this.addTrigger);
    }
  }

  onSearch(text: string): void {
    this.searchText.set(text);
    this.selected.set(null);
    this.search$.next(text.trim());
  }

  setFreeText(value: boolean): void {
    this.freeText.set(value);
    this.addError.set(null);
  }

  add(): void {
    const quantity = parseQuantity(this.quantity());
    const catalog = this.selected();
    if (!this.canAdd() || quantity === null) {
      return;
    }
    const material: NewMaterial =
      !this.freeText() && catalog !== null
        ? { quantity, catalogItemId: catalog.id }
        : { quantity, description: this.description().trim(), unit: this.unit().trim() };
    this.request(
      'add',
      this.visits.addMaterial(this.visit().visitId, material),
      (message) => this.addError.set(message),
      () => this.closeAdd(),
    );
  }

  private commit(row: MaterialRow, value: number, trigger: HTMLElement): void {
    if (row.kind === 'additional' && value === 0) {
      this.removeTrigger = trigger;
      this.error.set(null);
      this.removeTarget.set(row);
      return;
    }
    this.send(row, value);
  }

  private send(row: MaterialRow, value: number, onSuccess?: () => void): void {
    const visitId = this.visit().visitId;
    this.request(
      `row:${row.id}`,
      row.kind === 'planned'
        ? this.visits.setPlannedUsed(visitId, row.id, value)
        : this.visits.setMaterialQuantity(visitId, row.id, value),
      (message) => {
        this.error.set(message);
        this.closeRemove();
      },
      () => {
        this.clearDraft(row.id);
        onSuccess?.();
      },
    );
  }

  private request(
    key: string,
    request: Observable<TechnicianVisitDetail>,
    onError: (message: string) => void,
    onSuccess: () => void,
  ): void {
    if (this.pending() !== null) {
      return;
    }
    this.pending.set(key);
    this.error.set(null);
    this.addError.set(null);
    request.subscribe({
      next: (visit) => {
        this.pending.set(null);
        onSuccess();
        this.visitChange.emit(visit);
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error, { invalid: QUANTITY_INVALID_MESSAGE });
        this.pending.set(null);
        onError(failure.message);
        if (failure.reload) {
          this.reload.emit();
        }
      },
    });
  }

  private clearDraft(id: string): void {
    this.drafts.update((drafts) =>
      Object.fromEntries(Object.entries(drafts).filter(([key]) => key !== id)),
    );
  }

  private restoreFocus(target: HTMLElement | null): void {
    // A removed row no longer exists; focus falls back to Add material.
    setTimeout(() => (target?.isConnected ? target : this.addButton()?.nativeElement)?.focus());
  }
}
