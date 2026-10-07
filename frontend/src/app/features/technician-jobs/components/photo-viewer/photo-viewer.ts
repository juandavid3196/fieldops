import { Component, computed, input, model, output, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';

import { VisitPhoto } from '../visit-photos/visit-photos';

/** BR-17 full-size viewer with close and previous/next; the page returns focus on `closed`. */
@Component({
  selector: 'app-photo-viewer',
  imports: [ButtonDirective, Dialog],
  templateUrl: './photo-viewer.html',
  styleUrl: './photo-viewer.scss',
})
export class PhotoViewer {
  readonly photos = input.required<readonly VisitPhoto[]>();
  /** Index of the open photo; `null` keeps the viewer closed. */
  readonly index = model.required<number | null>();
  readonly closed = output<void>();

  private readonly failed = signal<ReadonlySet<string>>(new Set());

  readonly visible = computed(() => this.index() !== null);
  readonly photo = computed(() => {
    const index = this.index();
    return index === null ? null : (this.photos()[index] ?? null);
  });
  readonly alt = computed(
    () => `Assessment photo ${(this.index() ?? 0) + 1} of ${this.photos().length}`,
  );
  readonly unavailable = computed(() => {
    const photo = this.photo();
    return photo !== null && this.failed().has(photo.id);
  });

  step(delta: number): void {
    const index = this.index();
    const count = this.photos().length;
    if (index !== null && count > 1) {
      this.index.set((index + delta + count) % count);
    }
  }

  markFailed(): void {
    const photo = this.photo();
    if (photo !== null) {
      this.failed.update((ids) => new Set(ids).add(photo.id));
    }
  }

  onKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') {
      this.step(-1);
    } else if (event.key === 'ArrowRight') {
      this.step(1);
    }
  }

  /** Dialog hide (close button or Escape). */
  close(): void {
    if (this.index() !== null) {
      this.index.set(null);
      this.closed.emit();
    }
  }
}
