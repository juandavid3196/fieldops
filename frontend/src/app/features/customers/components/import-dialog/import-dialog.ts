import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
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
import { Subscription } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { ImportPreview, ImportRowError } from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import {
  CSV_SIZE_MESSAGE,
  CSV_TYPE_MESSAGE,
  saveCsv,
  validateCsvFile,
} from '../../utils/customer-format';

export const TEMPLATE_FAILED_MESSAGE = "We couldn't download the template. Try again.";
export const IMPORT_FAILED_MESSAGE = "We couldn't import the file. Try again.";
const CHECK_FAILED_MESSAGE = "We couldn't check the file. Try again.";
const MAX_SHOWN_ERRORS = 100;

/**
 * Import dialog (BR-17, Owner only): template download, CSV chooser, automatic validated preview
 * (row-error table or "N customers ready to import" with non-blocking duplicate warnings) and an
 * all-or-nothing confirmation. The page toasts and refreshes on `imported`.
 */
@Component({
  selector: 'app-import-dialog',
  imports: [ButtonDirective, Dialog, SpinnerIcon],
  templateUrl: './import-dialog.html',
  styleUrl: './import-dialog.scss',
})
export class ImportDialog {
  private readonly customers = inject(CustomersService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private previewRequest: Subscription | null = null;

  readonly visible = model(false);
  readonly imported = output<number>();
  readonly unauthorized = output<void>();

  readonly file = signal<File | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly preview = signal<ImportPreview | null>(null);
  readonly confirmErrors = signal<readonly ImportRowError[]>([]);
  readonly checking = signal(false);
  readonly importing = signal(false);
  readonly downloading = signal(false);
  readonly dragOver = signal(false);

  readonly rowErrors = computed<readonly ImportRowError[]>(() =>
    (this.confirmErrors().length > 0
      ? this.confirmErrors()
      : (this.preview()?.rowErrors ?? [])
    ).slice(0, MAX_SHOWN_ERRORS),
  );
  readonly warnings = computed(() => this.preview()?.duplicateWarnings ?? []);
  readonly busy = computed(() => this.checking() || this.importing());
  readonly canImport = computed(
    () =>
      this.file() !== null &&
      this.preview() !== null &&
      this.rowErrors().length === 0 &&
      !this.busy(),
  );
  readonly readyText = computed(() => {
    const count = this.preview()?.validRowCount ?? 0;
    return `${count.toLocaleString('en-US')} ${count === 1 ? 'customer' : 'customers'} ready to import`;
  });
  readonly errorsCapped = computed(() => {
    const total = this.confirmErrors().length || (this.preview()?.rowErrors.length ?? 0);
    return total >= MAX_SHOWN_ERRORS;
  });

  constructor() {
    let wasVisible = false;
    effect(() => {
      const isVisible = this.visible();
      if (isVisible && !wasVisible) {
        untracked(() => this.reset());
      }
      wasVisible = isVisible;
    });
    this.destroyRef.onDestroy(() => this.previewRequest?.unsubscribe());
  }

  private reset(): void {
    this.previewRequest?.unsubscribe();
    this.file.set(null);
    this.fileError.set(null);
    this.preview.set(null);
    this.confirmErrors.set([]);
    this.checking.set(false);
    this.importing.set(false);
  }

  browse(): void {
    this.fileInput()?.nativeElement.click();
  }

  removeFile(): void {
    this.reset();
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
    if (!this.busy()) {
      this.dragOver.set(true);
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    if (!this.busy()) {
      this.choose(event.dataTransfer?.files?.item(0) ?? null);
    }
  }

  onFileSelected(event: Event): void {
    this.choose((event.target as HTMLInputElement).files?.item(0) ?? null);
  }

  private choose(chosen: File | null): void {
    this.previewRequest?.unsubscribe();
    this.preview.set(null);
    this.confirmErrors.set([]);
    this.checking.set(false);
    if (chosen === null) {
      this.file.set(null);
      return;
    }
    const message = validateCsvFile(chosen);
    this.fileError.set(message);
    if (message !== null) {
      this.file.set(null);
      return;
    }
    this.file.set(chosen);
    this.runPreview(chosen);
  }

  private runPreview(chosen: File): void {
    this.checking.set(true);
    this.previewRequest = this.customers.importPreview(chosen).subscribe({
      next: (preview) => {
        this.checking.set(false);
        this.preview.set(preview);
      },
      error: (error: unknown) => {
        this.checking.set(false);
        this.handleFailed(error, CHECK_FAILED_MESSAGE);
      },
    });
  }

  downloadTemplate(): void {
    if (this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.customers
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
    if (chosen === null || !this.canImport()) {
      return;
    }
    this.fileError.set(null);
    this.importing.set(true);
    this.customers
      .import(chosen)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.importing.set(false);
          this.visible.set(false);
          this.imported.emit(result.importedCount);
        },
        error: (error: unknown) => {
          this.importing.set(false);
          this.handleFailed(error, IMPORT_FAILED_MESSAGE);
        },
      });
  }

  private handleFailed(error: unknown, fallback: string): void {
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (apiError?.rowErrors !== undefined && apiError.rowErrors.length > 0) {
      this.confirmErrors.set(apiError.rowErrors);
      return;
    }
    if (apiError?.status === 413) {
      this.fileError.set(CSV_SIZE_MESSAGE);
    } else if (apiError?.status === 415) {
      this.fileError.set(CSV_TYPE_MESSAGE);
    } else if (apiError?.fieldErrors['file']?.[0] !== undefined) {
      this.fileError.set(apiError.fieldErrors['file'][0]);
    } else {
      this.messageService.add({ severity: 'error', summary: fallback });
    }
  }

  close(): void {
    if (!this.importing()) {
      this.visible.set(false);
    }
  }
}
