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
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import {
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  merge,
  of,
  switchMap,
} from 'rxjs';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { CatalogRow } from '../../../products-services/models/catalog.model';
import { CatalogItemsService } from '../../../products-services/services/catalog-items.service';
import { LineType } from '../../models/quote.model';

export interface PickerTarget {
  /** Fixed by Add service / Add material; `null` lets the optional picker choose. */
  readonly type: LineType | null;
  readonly optional: boolean;
}

export interface PickedLine {
  /** `null` is the Custom line option. */
  readonly item: CatalogRow | null;
  readonly type: LineType;
}

type PickerState = 'loading' | 'ready' | 'error';

export const CATALOG_ERROR_MESSAGE = "We couldn't load the catalog.";
const SEARCH_DEBOUNCE_MS = 300;

/**
 * Catalog picker (BR-09): active catalog services and products through the existing
 * `GET /catalog-items` search, plus a Custom line option. The optional picker adds a
 * Service/Material choice.
 */
@Component({
  selector: 'app-catalog-picker-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Message, Skeleton],
  templateUrl: './catalog-picker-dialog.html',
  styleUrl: './catalog-picker-dialog.scss',
})
export class CatalogPickerDialog {
  private readonly catalog = inject(CatalogItemsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly target = input.required<PickerTarget | null>();
  readonly currency = input.required<string>();

  readonly picked = output<PickedLine>();
  readonly dismissed = output<void>();

  readonly search = signal('');
  readonly state = signal<PickerState>('loading');
  readonly items = signal<readonly CatalogRow[]>([]);
  /** The Service/Material choice of an optional item. */
  readonly choice = signal<LineType>('service');
  readonly errorMessage = CATALOG_ERROR_MESSAGE;

  readonly open = computed(() => this.target() !== null);
  readonly title = computed(() => {
    const target = this.target();
    if (target === null) {
      return '';
    }
    return target.optional
      ? 'Add optional item'
      : target.type === 'product'
        ? 'Add material'
        : 'Add service';
  });
  readonly type = computed<LineType>(() => this.target()?.type ?? this.choice());
  readonly customLabel = computed(
    () => `Custom ${this.type() === 'product' ? 'material' : 'service'} line`,
  );

  private readonly reload = new Subject<void>();

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.search.set('');
          this.choice.set('service');
          this.reload.next();
        });
      }
    });

    merge(
      this.reload.pipe(map(() => 0)),
      toObservable(this.search).pipe(distinctUntilChanged(), debounceTime(SEARCH_DEBOUNCE_MS)),
      toObservable(this.type),
    )
      .pipe(
        switchMap(() => {
          if (!this.open()) {
            return of(null);
          }
          this.state.set('loading');
          return this.catalog
            .list({
              type: this.type(),
              search: this.search(),
              taxStatus: null,
              status: 'active',
              sort: 'name',
              page: 1,
            })
            .pipe(
              map((response) => response.items),
              catchError(() => of(undefined)),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (result === null) {
          return;
        }
        if (result === undefined) {
          this.state.set('error');
          return;
        }
        this.items.set(result);
        this.state.set('ready');
      });
  }

  retry(): void {
    this.reload.next();
  }

  price(item: CatalogRow): string {
    return formatMoney(item.unitPrice, this.currency());
  }

  pick(item: CatalogRow | null): void {
    this.picked.emit({ item, type: this.type() });
  }
}
