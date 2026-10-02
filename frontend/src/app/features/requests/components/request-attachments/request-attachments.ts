import {
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Subscription } from 'rxjs';

import { MAX_FILES, validateNewFile } from '../../../service-request/service-request.validators';
import { RequestAttachment } from '../../models/requests.model';
import { RequestsService } from '../../services/requests.service';
import { fileSize } from '../../utils/requests-format';

const ACCEPT = '.jpg,.jpeg,.png,.pdf';

/**
 * Attachments grid (BR-16): image thumbnails and PDF icons fetched through the authenticated
 * endpoint as blobs. Object URLs are revoked when an attachment disappears or the view is
 * destroyed. Uploads are validated per file before the page sends them.
 */
@Component({
  selector: 'app-request-attachments',
  templateUrl: './request-attachments.html',
  styleUrl: './request-attachments.scss',
})
export class RequestAttachments {
  private readonly requests = inject(RequestsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly requestId = input.required<string>();
  readonly attachments = input.required<readonly RequestAttachment[]>();
  readonly canUpload = input(false);
  readonly uploading = input(false);
  /** Server-side per-file or per-call rejections of the last upload. */
  readonly serverErrors = input<readonly string[]>([]);

  readonly filesSelected = output<File[]>();

  readonly accept = ACCEPT;
  readonly maxFiles = MAX_FILES;
  readonly size = fileSize;
  /** Rejections of files dropped before sending. */
  readonly clientErrors = signal<readonly string[]>([]);
  readonly thumbnails = signal<Readonly<Record<string, string>>>({});
  readonly failedThumbnails = signal<ReadonlySet<string>>(new Set());

  private readonly loads = new Map<string, Subscription>();

  constructor() {
    effect(() => {
      const requestId = this.requestId();
      const images = this.attachments().filter((file) => file.mimeType.startsWith('image/'));
      untracked(() => this.syncThumbnails(requestId, images));
    });
    this.destroyRef.onDestroy(() => {
      this.loads.forEach((load) => load.unsubscribe());
      Object.values(this.thumbnails()).forEach((url) => URL.revokeObjectURL(url));
    });
  }

  get canAdd(): boolean {
    return this.canUpload() && this.attachments().length < MAX_FILES;
  }

  private syncThumbnails(requestId: string, images: readonly RequestAttachment[]): void {
    const wanted = new Set(images.map((file) => file.id));
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
    for (const file of images) {
      if (this.thumbnails()[file.id] !== undefined || this.loads.has(file.id)) {
        continue;
      }
      this.loads.set(
        file.id,
        this.requests.downloadAttachment(requestId, file.id).subscribe({
          next: (blob) => {
            this.loads.delete(file.id);
            this.thumbnails.update((map) => ({ ...map, [file.id]: URL.createObjectURL(blob) }));
          },
          error: () => {
            this.loads.delete(file.id);
            this.failedThumbnails.update((set) => new Set(set).add(file.id));
          },
        }),
      );
    }
  }

  /** Images open as a preview; PDFs download (BR-16 `Content-Disposition`). */
  open(file: RequestAttachment): void {
    const preview = this.thumbnails()[file.id];
    if (file.mimeType.startsWith('image/') && preview !== undefined) {
      window.open(preview, '_blank', 'noopener');
      return;
    }
    this.requests.downloadAttachment(this.requestId(), file.id).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = file.fileName;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url));
    });
  }

  onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    input.value = '';
    const accepted: File[] = [];
    const errors: string[] = [];
    const known = this.attachments().map((file) => ({ name: file.fileName, size: file.sizeBytes }));
    for (const file of picked) {
      const reason = validateNewFile([...known, ...accepted], file);
      if (reason === null) {
        accepted.push(file);
      } else {
        errors.push(reason);
      }
    }
    this.clientErrors.set(errors);
    if (accepted.length > 0) {
      this.filesSelected.emit(accepted);
    }
  }
}
