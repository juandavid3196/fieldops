import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';

import { AssessmentPhoto } from '../../models/requests.model';
import { RequestsService } from '../../services/requests.service';

/**
 * Thumbnails of the completed assessment photos (BR-03). Content is fetched through the
 * authenticated endpoint as blobs (no public URL exists); each thumbnail opens its photo. Object
 * URLs are revoked when a photo disappears or the view is destroyed.
 */
@Component({
  selector: 'app-assessment-photos',
  templateUrl: './assessment-photos.html',
  styleUrl: './assessment-photos.scss',
})
export class AssessmentPhotos {
  private readonly requests = inject(RequestsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly requestId = input.required<string>();
  readonly assessmentId = input.required<string>();
  readonly photos = input.required<readonly AssessmentPhoto[]>();

  readonly thumbnails = signal<Readonly<Record<string, string>>>({});
  private readonly loads = new Map<string, Subscription>();

  constructor() {
    effect(() => {
      const requestId = this.requestId();
      const assessmentId = this.assessmentId();
      const photos = this.photos();
      untracked(() => this.sync(requestId, assessmentId, photos));
    });
    this.destroyRef.onDestroy(() => {
      this.loads.forEach((load) => load.unsubscribe());
      Object.values(this.thumbnails()).forEach((url) => URL.revokeObjectURL(url));
    });
  }

  private sync(requestId: string, assessmentId: string, photos: readonly AssessmentPhoto[]): void {
    const wanted = new Set(photos.map((photo) => photo.id));
    const current = this.thumbnails();
    const stale = Object.keys(current).filter((id) => !wanted.has(id));
    if (stale.length > 0) {
      stale.forEach((id) => URL.revokeObjectURL(current[id]));
      this.thumbnails.set(
        Object.fromEntries(Object.entries(current).filter(([id]) => wanted.has(id))),
      );
    }
    for (const [id, load] of this.loads) {
      if (!wanted.has(id)) {
        load.unsubscribe();
        this.loads.delete(id);
      }
    }
    for (const photo of photos) {
      if (this.thumbnails()[photo.id] !== undefined || this.loads.has(photo.id)) {
        continue;
      }
      this.loads.set(
        photo.id,
        this.requests.downloadAssessmentPhoto(requestId, assessmentId, photo.id).subscribe({
          next: (blob) => {
            this.loads.delete(photo.id);
            this.thumbnails.update((map) => ({ ...map, [photo.id]: URL.createObjectURL(blob) }));
          },
          error: () => this.loads.delete(photo.id),
        }),
      );
    }
  }

  open(photoId: string): void {
    const url = this.thumbnails()[photoId];
    if (url !== undefined) {
      window.open(url, '_blank', 'noopener');
    }
  }
}
