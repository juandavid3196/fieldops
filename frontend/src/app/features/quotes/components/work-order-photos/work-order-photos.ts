import { Component, computed, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { ChevronLeftIcon } from 'primeng/icons/chevronleft';
import { ChevronRightIcon } from 'primeng/icons/chevronright';
import { SearchIcon } from 'primeng/icons/search';
import { Skeleton } from 'primeng/skeleton';

export interface WorkOrderPhoto {
  readonly id: string;
  readonly state: 'loading' | 'ready' | 'failed';
  readonly url: string | null;
}

/**
 * Assessment photo thumbnails with the accessible full-size viewer (previous/next; Escape
 * closes). Presentational: the page loads the authenticated blobs once and shares them between
 * the assessment card and the attachments (BR-06, BR-23).
 */
@Component({
  selector: 'app-work-order-photos',
  imports: [ButtonDirective, Dialog, ChevronLeftIcon, ChevronRightIcon, SearchIcon, Skeleton],
  templateUrl: './work-order-photos.html',
  styleUrl: './work-order-photos.scss',
})
export class WorkOrderPhotos {
  readonly items = input.required<readonly WorkOrderPhoto[]>();

  readonly viewing = signal<number | null>(null);
  readonly viewed = computed(() => {
    const index = this.viewing();
    const item = index === null ? undefined : this.items()[index];
    return item?.url ? { index: index as number, url: item.url } : null;
  });

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
}
