import { Component, input, output, signal } from '@angular/core';

export interface VisitPhoto {
  readonly id: string;
  readonly url: string;
}

export interface PhotoOpenRequest {
  readonly index: number;
  /** Element to focus again when the viewer closes. */
  readonly trigger: HTMLElement;
}

/** BR-17 read-only thumbnails of the approved assessment photos. */
@Component({
  selector: 'app-visit-photos',
  templateUrl: './visit-photos.html',
  styleUrl: './visit-photos.scss',
})
export class VisitPhotos {
  readonly photos = input.required<readonly VisitPhoto[]>();
  readonly open = output<PhotoOpenRequest>();

  readonly failed = signal<ReadonlySet<string>>(new Set());

  alt(index: number): string {
    return `Assessment photo ${index + 1} of ${this.photos().length}`;
  }

  markFailed(id: string): void {
    this.failed.update((current) => new Set(current).add(id));
  }
}
