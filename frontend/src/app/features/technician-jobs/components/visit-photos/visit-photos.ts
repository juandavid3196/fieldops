import { Component, input, output, signal } from '@angular/core';

export interface VisitPhoto {
  readonly id: string;
  readonly url: string;
  /** Job photos carry Before/After; assessment photos have none. */
  readonly label?: string;
}

/** "<label> photo <k> of <n>" counting within the label, or "Assessment photo <k> of <n>". */
export function photoAlt(photos: readonly VisitPhoto[], index: number): string {
  const label = photos[index]?.label;
  const group = label === undefined ? photos : photos.filter((photo) => photo.label === label);
  const position = group.indexOf(photos[index]) + 1;
  return `${label ?? 'Assessment'} photo ${position} of ${group.length}`;
}

export interface PhotoOpenRequest {
  readonly index: number;
  /** Element to focus again when the viewer closes. */
  readonly trigger: HTMLElement;
}

/** BR-17 read-only thumbnails of the assessment or job photos, with their Before/After label. */
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
    return photoAlt(this.photos(), index);
  }

  markFailed(id: string): void {
    this.failed.update((current) => new Set(current).add(id));
  }
}
