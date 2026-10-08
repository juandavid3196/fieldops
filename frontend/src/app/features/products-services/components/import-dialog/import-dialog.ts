import {
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { ApiError, ApiRowError, isApiError } from '../../../../core/models/api-error.model';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { CatalogItemsService } from '../../services/catalog-items.service';
import { CSV_SIZE_MESSAGE, CSV_TYPE_MESSAGE, validateCsvFile } from '../../utils/catalog-format';

export const TEMPLATE_FAILED_MESSAGE = "We couldn't download the template. Try again.";
export const IMPORT_CONFLICT_MESSAGE = 'Some items already exist. Review the file and try again.';
const UNEXPECTED_MESSAGE = 'An unexpected error occurred. Please try again.';

/**
 * Import dialog (FR-14, Owner only): template link, CSV chooser, **Cancel** / **Import**. A
 * `400` keeps the dialog open and lists the file error or the row errors; success closes it and
 * tells the page to toast and refresh.
 */
@Component({
  selector: 'app-import-dialog',
  imports: [ButtonDirective, Dialog, SpinnerIcon],
  templateUrl: './import-dialog.html',
  styleUrl: './import-dialog.scss',
})
export class ImportDialog {
  private readonly catalog = inject(CatalogItemsService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  readonly visible = model(false);
  readonly imported = output<number>();
  readonly unauthorized = output<void>();
  /** Fired after the dialog closed (focus goes back to the opener). */
  readonly closed = output<void>();

  readonly file = signal<File | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly rowErrors = signal<readonly ApiRowError[]>([]);
  readonly importing = signal(false);
  readonly downloading = signal(false);
  readonly dragOver = signal(false);

  constructor() {
    let wasVisible = false;
    effect(() => {
      const isVisible = this.visible();
      if (isVisible && !wasVisible) {
        untracked(() => this.reset());
      }
      wasVisible = isVisible;
    });
  }

  private reset(): void {
    this.file.set(null);
    this.fileError.set(null);
    this.rowErrors.set([]);
    this.importing.set(false);
  }

  browse(): void {
    this.fileInput()?.nativeElement.click();
  }

  removeFile(): void {
    this.file.set(null);
    this.fileError.set(null);
    this.rowErrors.set([]);
    const input = this.fileInput()?.nativeElement;
    if (input) {
      input.value = '';
    }
  }

  sizeText(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (!this.importing()) {
      this.dragOver.set(true);
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    if (!this.importing()) {
      this.choose(event.dataTransfer?.files?.item(0) ?? null);
    }
  }

  onFileSelected(event: Event): void {
    this.choose((event.target as HTMLInputElement).files?.item(0) ?? null);
  }

  private choose(chosen: File | null): void {
    this.rowErrors.set([]);
    if (chosen === null) {
      this.file.set(null);
      return;
    }
    const message = validateCsvFile(chosen);
    this.fileError.set(message);
    this.file.set(message === null ? chosen : null);
  }

  downloadTemplate(): void {
    if (this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.catalog
      .importTemplate()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (csv) => {
          this.downloading.set(false);
          saveCsv(csv);
        },
        error: (error: unknown) => {
          this.downloading.set(false);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.unauthorized.emit();
            return;
          }
          this.messageService.add({ severity: 'error', summary: TEMPLATE_FAILED_MESSAGE });
        },
      });
  }

  submit(): void {
    const chosen = this.file();
    if (chosen === null || this.importing()) {
      return;
    }
    this.fileError.set(null);
    this.rowErrors.set([]);
    this.importing.set(true);
    this.catalog
      .import(chosen)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.importing.set(false);
          this.visible.set(false);
          this.imported.emit(result.importedCount);
        },
        error: (error: unknown) => this.handleFailed(error),
      });
  }

  private handleFailed(error: unknown): void {
    this.importing.set(false);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.rowErrors !== undefined) {
      this.rowErrors.set(apiError.rowErrors);
      return;
    }
    if (apiError?.status === 413) {
      this.fileError.set(CSV_SIZE_MESSAGE);
    } else if (apiError?.status === 415) {
      this.fileError.set(CSV_TYPE_MESSAGE);
    } else if (apiError?.kind === 'conflict') {
      this.fileError.set(IMPORT_CONFLICT_MESSAGE);
    } else {
      this.fileError.set(
        apiError?.fieldErrors['file']?.[0] ?? apiError?.message ?? UNEXPECTED_MESSAGE,
      );
    }
  }

  close(): void {
    if (!this.importing()) {
      this.visible.set(false);
    }
  }
}
