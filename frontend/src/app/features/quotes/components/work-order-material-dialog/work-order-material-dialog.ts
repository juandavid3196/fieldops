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
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';

import { MaterialSource } from '../../../jobs/models/work-order.model';
import { SOURCE_LABELS, optionsOf } from '../../../jobs/utils/work-order-format';
import { CatalogRow } from '../../../products-services/models/catalog.model';
import { CatalogItemsService } from '../../../products-services/services/catalog-items.service';
import {
  MaterialErrors,
  MaterialField,
  MaterialRow,
  materialErrors,
} from '../../utils/work-order-form';

export type MaterialValue = Omit<MaterialRow, 'uid'>;

const SEARCH_DEBOUNCE_MS = 300;

/**
 * Add / edit material dialog (BR-12): pick an active catalog product (prefills description and
 * unit) or type a manual description; then quantity, unit and source. No price is ever shown.
 */
@Component({
  selector: 'app-work-order-material-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Select, SpinnerIcon],
  templateUrl: './work-order-material-dialog.html',
  styleUrl: './work-order-material-dialog.scss',
})
export class WorkOrderMaterialDialog {
  private readonly catalog = inject(CatalogItemsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  /** The row being edited; `null` adds a new one. */
  readonly material = input.required<MaterialRow | null>();

  readonly saved = output<MaterialValue>();
  readonly dismissed = output<void>();

  readonly sources = optionsOf(SOURCE_LABELS);
  readonly title = computed(() => (this.material() === null ? 'Add material' : 'Edit material'));

  readonly search = signal('');
  readonly searching = signal(false);
  readonly products = signal<readonly CatalogRow[]>([]);
  readonly description = signal('');
  readonly quantity = signal<number | null>(1);
  readonly unit = signal('unit');
  readonly source = signal<MaterialSource>('truck_stock');
  private readonly links = signal<{ quoteLineId: string | null; catalogItemId: string | null }>({
    quoteLineId: null,
    catalogItemId: null,
  });
  readonly errors = signal<MaterialErrors>({});

  private readonly searches = new Subject<void>();

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });

    this.searches
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        switchMap(() => {
          this.searching.set(true);
          return this.catalog
            .list({
              type: 'product',
              search: this.search(),
              taxStatus: null,
              status: 'active',
              sort: 'name',
              page: 1,
            })
            .pipe(
              map((response) => response.items),
              catchError(() => of<readonly CatalogRow[]>([])),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((items) => {
        this.products.set(items);
        this.searching.set(false);
      });
  }

  private reset(): void {
    const material = this.material();
    this.search.set('');
    this.errors.set({});
    this.description.set(material?.description ?? '');
    this.quantity.set(material?.quantity ?? 1);
    this.unit.set(material?.unit ?? 'unit');
    this.source.set(material?.source ?? 'truck_stock');
    this.links.set({
      quoteLineId: material?.quoteLineId ?? null,
      catalogItemId: material?.catalogItemId ?? null,
    });
    this.searches.next();
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.searches.next();
  }

  pick(product: CatalogRow): void {
    this.links.update((links) => ({ ...links, catalogItemId: product.id }));
    this.description.set(product.name.slice(0, 240));
    this.unit.set('unit');
    this.clear('description', 'unit');
  }

  onDescription(value: string): void {
    this.description.set(value);
    this.clear('description');
  }

  onQuantity(value: number | null): void {
    this.quantity.set(value);
    this.clear('quantity');
  }

  onUnit(value: string): void {
    this.unit.set(value);
    this.clear('unit');
  }

  private clear(...fields: MaterialField[]): void {
    this.errors.update((errors) =>
      Object.fromEntries(
        Object.entries(errors).filter(([key]) => !fields.includes(key as MaterialField)),
      ),
    );
  }

  save(): void {
    const value: MaterialValue = {
      quoteLineId: this.links().quoteLineId,
      catalogItemId: this.links().catalogItemId,
      description: this.description(),
      quantity: this.quantity(),
      unit: this.unit(),
      source: this.source(),
    };
    const errors = materialErrors(value);
    this.errors.set(errors);
    if (Object.keys(errors).length === 0) {
      this.saved.emit(value);
    }
  }
}
