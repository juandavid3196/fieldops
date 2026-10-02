import {
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

@Component({
  selector: 'app-attachment-dropzone',
  templateUrl: './attachment-dropzone.html',
  styleUrl: './attachment-dropzone.scss',
})
export class AttachmentDropzone {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly dragging = signal(false);
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');
  private readonly previews = new Map<File, string>();

  constructor() {
    // Preview URLs live only while their file is listed; removed files and teardown revoke them.
    effect(() => {
      const current = new Set(this.store.files());
      for (const [file, url] of this.previews) {
        if (!current.has(file)) {
          URL.revokeObjectURL(url);
          this.previews.delete(file);
        }
      }
    });
    inject(DestroyRef).onDestroy(() => {
      for (const url of this.previews.values()) URL.revokeObjectURL(url);
      this.previews.clear();
    });
  }

  protected browse(): void {
    this.picker().nativeElement.click();
  }

  protected picked(): void {
    const input = this.picker().nativeElement;
    this.store.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.store.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected over(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  /** Object URL for an image file, `null` for PDFs (a file icon is shown instead). */
  protected preview(file: File): string | null {
    if (!file.type.startsWith('image/') || typeof URL.createObjectURL !== 'function') return null;
    let url = this.previews.get(file);
    if (url === undefined) {
      url = URL.createObjectURL(file);
      this.previews.set(file, url);
    }
    return url;
  }

  protected size(bytes: number): string {
    return bytes >= 1024 * 1024
      ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
      : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }
}
