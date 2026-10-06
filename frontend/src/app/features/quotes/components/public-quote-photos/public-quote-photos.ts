import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { ChevronLeftIcon } from 'primeng/icons/chevronleft';
import { ChevronRightIcon } from 'primeng/icons/chevronright';
import { SearchIcon } from 'primeng/icons/search';
import { Skeleton } from 'primeng/skeleton';

import { PublicQuoteService } from '../../services/public-quote.service';

type PhotoState = 'loading' | 'ready' | 'failed';

interface PhotoItem {
  readonly id: string;
  readonly state: PhotoState;
  readonly url: string | null;
}

/**
 * Assessment photos of the quote link (BR-07): token-protected blobs shown as object URLs, with an
 * accessible full-size viewer (previous/next; Escape closes). Object URLs are revoked on destroy.
 */
@Component({
  selector: 'app-public-quote-photos',
  imports: [ButtonDirective, Dialog, ChevronLeftIcon, ChevronRightIcon, SearchIcon, Skeleton],
  templateUrl: './public-quote-photos.html',
  styleUrl: './public-quote-photos.scss',
})
export class PublicQuotePhotos {
  private readonly service = inject(PublicQuoteService);
  private readonly urls: string[] = [];

  readonly photos = input.required<readonly { readonly id: string }[]>();

  readonly items = signal<readonly PhotoItem[]>([]);
  readonly viewing = signal<number | null>(null);

  readonly viewed = computed(() => {
    const index = this.viewing();
    const item = index === null ? undefined : this.items()[index];
    return item?.url ? { index: index as number, url: item.url } : null;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.urls.forEach((url) => URL.revokeObjectURL(url)));
    effect(() => {
      const ids = this.photos().map((photo) => photo.id);
      this.items.set(ids.map((id) => ({ id, state: 'loading', url: null })));
      ids.forEach((id, index) => this.load(id, index));
    });
  }

  alt(index: number): string {
    return `Assessment photo ${index + 1} of ${this.items().length}`;
  }

  open(index: number): void {
    this.viewing.set(index);
  }

  close(): void {
    this.viewing.set(null);
  }

  step(delta: number): void {
    const current = this.viewing();
    const count = this.items().length;
    if (current !== null && count > 0) {
      this.viewing.set((current + delta + count) % count);
    }
  }

  onKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') {
      this.step(-1);
    } else if (event.key === 'ArrowRight') {
      this.step(1);
    }
  }

  private load(id: string, index: number): void {
    this.service.photo(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        this.urls.push(url);
        this.patch(id, index, { state: 'ready', url });
      },
      error: () => this.patch(id, index, { state: 'failed', url: null }),
    });
  }

  private patch(id: string, index: number, change: Pick<PhotoItem, 'state' | 'url'>): void {
    this.items.update((items) =>
      items[index]?.id === id
        ? items.map((item, position) => (position === index ? { id, ...change } : item))
        : items,
    );
  }
}
