import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  model,
  signal,
  untracked,
} from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { PublicPhoto } from '../../models/invoice.model';
import { InvoiceLinkApi } from '../../services/invoice-link-api';
import { formatCalendarDate } from '../../utils/public-invoice-format';

interface PhotoImage {
  readonly state: 'loading' | 'ready' | 'error';
  readonly url: string | null;
}

/**
 * Before/after photo gallery dialog (BR-27): the list loads when it opens, each image is fetched
 * lazily as a blob when first shown, and every object URL is revoked when the dialog closes.
 */
@Component({
  selector: 'app-public-photo-gallery',
  imports: [ButtonDirective, Dialog, SpinnerIcon],
  templateUrl: './public-photo-gallery.html',
  styleUrl: './public-photo-gallery.scss',
})
export class PublicPhotoGallery {
  private readonly service = inject(InvoiceLinkApi);

  readonly visible = model(false);

  protected readonly listState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly photos = signal<readonly PublicPhoto[]>([]);
  protected readonly index = signal(0);
  protected readonly images = signal<Readonly<Record<string, PhotoImage>>>({});

  protected readonly current = computed(() => {
    const photo = this.photos()[this.index()];
    return photo === undefined
      ? null
      : {
          photo,
          typeLabel: photo.type === 'before' ? 'Before' : 'After',
          date: formatCalendarDate(photo.takenOn),
          image: this.images()[photo.photoId] ?? { state: 'loading', url: null },
        };
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.loadList());
      } else {
        untracked(() => this.release());
      }
    });
    effect(() => {
      const photo = this.photos()[this.index()];
      if (photo !== undefined) {
        untracked(() => this.loadImage(photo));
      }
    });
    inject(DestroyRef).onDestroy(() => this.release());
  }

  protected loadList(): void {
    this.release();
    this.listState.set('loading');
    this.service.photos().subscribe({
      next: (photos) => {
        this.photos.set(photos);
        this.listState.set('ready');
      },
      error: () => this.listState.set('error'),
    });
  }

  protected loadImage(photo: PublicPhoto, force = false): void {
    const known = this.images()[photo.photoId];
    if (known !== undefined && !(force && known.state === 'error')) {
      return;
    }
    this.setImage(photo.photoId, { state: 'loading', url: null });
    this.service.photoContent(photo.photoId).subscribe({
      next: (blob) => {
        if (!this.visible()) {
          return;
        }
        this.setImage(photo.photoId, { state: 'ready', url: URL.createObjectURL(blob) });
      },
      error: () => this.setImage(photo.photoId, { state: 'error', url: null }),
    });
  }

  protected retryImage(photo: PublicPhoto): void {
    this.loadImage(photo, true);
  }

  protected move(step: number): void {
    const count = this.photos().length;
    if (count > 0) {
      this.index.update((value) => (value + step + count) % count);
    }
  }

  protected onKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      event.preventDefault();
      this.move(event.key === 'ArrowRight' ? 1 : -1);
    }
  }

  private setImage(id: string, image: PhotoImage): void {
    this.images.update((current) => ({ ...current, [id]: image }));
  }

  private release(): void {
    for (const image of Object.values(this.images())) {
      if (image.url !== null) {
        URL.revokeObjectURL(image.url);
      }
    }
    this.images.set({});
    this.photos.set([]);
    this.index.set(0);
  }
}
