import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { Button } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { EMPTY, Subject, catchError, filter, map, of, switchMap, timer } from 'rxjs';

import { GlobalSearchItem } from '../../../core/models/global-search.model';
import { isApiError } from '../../../core/models/api-error.model';
import { GlobalSearchService } from '../../../core/services/global-search.service';
import { SearchGroup, SearchRow, buildGroups } from './global-search-view';

export const SEARCH_PLACEHOLDER = 'Search requests, customers, work orders…';
export const SEARCH_DEBOUNCE_MS = 300;
export const SEARCH_MIN_LENGTH = 2;
export const SEARCH_HINT = 'Search by name, number, email, or phone.';
export const SEARCH_SHORT = 'Enter at least 2 characters.';
export const SEARCH_ERROR = "Couldn't search right now.";
export const SEARCH_MORE = 'More results exist. Refine your search.';

type SearchState = 'idle' | 'short' | 'loading' | 'results' | 'empty' | 'error';

interface Outcome {
  readonly query: string;
  readonly items: readonly GlobalSearchItem[] | null;
  readonly hasMore: boolean;
  readonly unauthorized: boolean;
}

/** global-search BR-10 – BR-17: staff top bar combobox with a grouped results panel. */
@Component({
  selector: 'app-global-search',
  imports: [Button, IconField, InputIcon, InputText],
  templateUrl: './global-search.html',
  styleUrl: './global-search.scss',
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown)': 'onDocumentKeydown($event)',
    '(focusout)': 'onFocusOut($event)',
  },
})
export class GlobalSearch {
  private readonly service = inject(GlobalSearchService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('field');
  private readonly requests = new Subject<{ query: string; delay: number }>();
  private trimmed = '';

  readonly placeholder = SEARCH_PLACEHOLDER;
  readonly hint = SEARCH_HINT;
  readonly shortText = SEARCH_SHORT;
  readonly errorText = SEARCH_ERROR;
  readonly moreText = SEARCH_MORE;
  readonly skeletonRows = [0, 1, 2, 3];

  readonly term = signal('');
  readonly open = signal(false);
  readonly activeIndex = signal(-1);
  readonly state = signal<SearchState>('idle');
  readonly groups = signal<readonly SearchGroup[]>([]);
  readonly hasMore = signal(false);
  readonly query = signal('');

  readonly options = computed<readonly SearchRow[]>(() =>
    this.groups().flatMap((group) => group.rows),
  );
  readonly countText = computed(() => {
    const count = this.options().length;
    return count === 1 ? '1 result' : `${count} results`;
  });
  readonly emptyText = computed(() => `No results for “${this.query()}”.`);
  readonly activeId = computed(() =>
    this.open() && this.state() === 'results'
      ? (this.options()[this.activeIndex()]?.id ?? null)
      : null,
  );
  readonly announcement = computed(() => {
    switch (this.state()) {
      case 'results':
        return this.countText();
      case 'empty':
        return this.emptyText();
      case 'error':
        return this.errorText;
      default:
        return '';
    }
  });

  constructor() {
    this.requests
      .pipe(
        switchMap(({ query, delay }) =>
          query.length < SEARCH_MIN_LENGTH
            ? EMPTY
            : timer(delay).pipe(
                switchMap(() =>
                  this.service.search(query).pipe(
                    map((response): Outcome => ({
                      query,
                      items: response.items,
                      hasMore: response.hasMore,
                      unauthorized: false,
                    })),
                    catchError((error: unknown) =>
                      of<Outcome>({
                        query,
                        items: null,
                        hasMore: false,
                        unauthorized: isApiError(error) && error.kind === 'unauthorized',
                      }),
                    ),
                  ),
                ),
              ),
        ),
        // BR-11: a response for a value that is no longer the current one is ignored.
        filter((outcome) => outcome.query === this.trimmed),
        takeUntilDestroyed(),
      )
      .subscribe((outcome) => this.apply(outcome));

    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.open.set(false));
  }

  onInput(value: string): void {
    this.term.set(value);
    this.open.set(true);
    const trimmed = value.trim();
    if (trimmed === this.trimmed) {
      return;
    }
    this.trimmed = trimmed;
    this.query.set(trimmed);
    this.activeIndex.set(-1);
    if (trimmed.length === 0) {
      this.reset('idle');
    } else if (trimmed.length < SEARCH_MIN_LENGTH) {
      this.reset('short');
    } else {
      this.state.set('loading');
    }
    this.requests.next({ query: trimmed, delay: SEARCH_DEBOUNCE_MS });
  }

  retry(): void {
    if (this.trimmed.length < SEARCH_MIN_LENGTH) {
      return;
    }
    this.state.set('loading');
    this.requests.next({ query: this.trimmed, delay: 0 });
  }

  onFieldOpen(): void {
    this.open.set(true);
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.options().length;
    const navigable = this.open() && this.state() === 'results' && count > 0;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        event.preventDefault();
        if (!this.open()) {
          this.open.set(true);
          return;
        }
        if (navigable) {
          const step = event.key === 'ArrowDown' ? 1 : -1;
          this.setActive((this.activeIndex() + step + count) % count, true);
        }
        break;
      }
      case 'Enter': {
        event.preventDefault();
        const row = navigable ? this.options()[this.activeIndex()] : undefined;
        if (row) {
          this.select(row);
        }
        break;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
        }
        this.open.set(false);
        break;
    }
  }

  setActive(index: number, scroll = false): void {
    this.activeIndex.set(index);
    if (scroll) {
      afterNextRender(
        () => {
          const id = this.options()[index]?.id;
          this.host.ownerDocument.getElementById(id ?? '')?.scrollIntoView?.({ block: 'nearest' });
        },
        { injector: this.injector },
      );
    }
  }

  select(row: SearchRow): void {
    this.open.set(false);
    this.term.set('');
    this.input().nativeElement.value = '';
    this.trimmed = '';
    this.query.set('');
    this.reset('idle');
    this.requests.next({ query: '', delay: 0 });
    void this.router.navigateByUrl(
      row.queryParams === null
        ? row.route
        : this.router.createUrlTree([row.route], { queryParams: row.queryParams }).toString(),
    );
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (this.open() && target instanceof Node && !this.host.contains(target)) {
      this.open.set(false);
    }
  }

  /** Tabbing out of the whole component closes it; moving into the panel (Retry) keeps it open. */
  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (this.open() && next instanceof Node && !this.host.contains(next)) {
      this.open.set(false);
    }
  }

  /** BR-17: Ctrl/Cmd + K focuses the field and selects its text. */
  onDocumentKeydown(event: KeyboardEvent): void {
    if (
      event.key.toLowerCase() !== 'k' ||
      !(event.ctrlKey || event.metaKey) ||
      event.altKey ||
      event.shiftKey
    ) {
      return;
    }
    event.preventDefault();
    const field = this.input().nativeElement;
    field.focus();
    field.select();
    this.open.set(true);
  }

  private apply(outcome: Outcome): void {
    if (outcome.unauthorized) {
      this.reset('idle');
      this.open.set(false);
    } else if (outcome.items === null) {
      this.reset('error');
    } else if (outcome.items.length === 0) {
      this.reset('empty');
    } else {
      this.groups.set(buildGroups(outcome.items));
      this.hasMore.set(outcome.hasMore);
      this.activeIndex.set(0);
      this.state.set('results');
    }
  }

  private reset(state: SearchState): void {
    this.groups.set([]);
    this.hasMore.set(false);
    this.activeIndex.set(-1);
    this.state.set(state);
  }
}
