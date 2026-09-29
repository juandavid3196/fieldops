import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonDirective } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { ProgressBar } from 'primeng/progressbar';
import { Skeleton } from 'primeng/skeleton';
import { Subscription } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { OrganizationLogoMetadata } from '../../models/company-settings.model';
import { OrganizationLogoService } from '../../services/organization-logo.service';
import {
  LOGO_ACCEPT,
  LOGO_HELPER,
  LOGO_SIZE_MESSAGE,
  LOGO_TYPE_MESSAGE,
  validateLogoFile,
} from '../../utils/logo-validation';

export const LOGO_UPDATED_MESSAGE = 'Logo updated';
export const LOGO_REMOVED_MESSAGE = 'Logo removed';

/**
 * Company logo (FR-10): preview, **Change logo** (immediate upload with progress) and
 * **Remove logo**. All state lives here, outside the organization form, so logo actions
 * never dirty it. The logo is only ever displayed through an `<img>` (BR-08).
 */
@Component({
  selector: 'app-logo-control',
  imports: [ButtonDirective, ProgressBar, Skeleton],
  templateUrl: './logo-control.html',
  styleUrl: './logo-control.scss',
})
export class LogoControl {
  private readonly logoService = inject(OrganizationLogoService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  /** Metadata from `GET /organization-settings`; a new value reloads the preview. */
  readonly logo = input.required<OrganizationLogoMetadata | null>();
  readonly displayName = input.required<string>();
  readonly canManage = input.required<boolean>();
  /** A `401` from any logo request; the page owns the redirect. */
  readonly unauthorized = output<void>();

  readonly accept = LOGO_ACCEPT;
  readonly helper = LOGO_HELPER;

  readonly hasLogo = signal(false);
  readonly previewUrl = signal<string | null>(null);
  readonly previewLoading = signal(false);
  readonly progress = signal<number | null>(null);
  readonly uploading = signal(false);
  readonly removing = signal(false);
  readonly error = signal<string | null>(null);

  readonly busy = computed(() => this.uploading() || this.removing());
  readonly altText = computed(() => `${this.displayName().trim()} logo`.trim());

  private previewRequest: Subscription | null = null;

  constructor() {
    effect(() => {
      const metadata = this.logo();
      untracked(() => this.loadPreview(metadata));
    });
    this.destroyRef.onDestroy(() => {
      this.previewRequest?.unsubscribe();
      this.setPreview(null);
    });
  }

  /** Validates client-side (BR-07 mirror), then uploads immediately. */
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    input.value = '';
    if (file === null || this.busy()) {
      return;
    }

    const message = validateLogoFile(file);
    if (message !== null) {
      this.error.set(message);
      return;
    }

    this.error.set(null);
    this.uploading.set(true);
    this.progress.set(0);
    this.logoService
      .upload(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (uploadEvent) => {
          if (uploadEvent.kind === 'progress') {
            this.progress.set(uploadEvent.percent);
            return;
          }
          this.previewRequest?.unsubscribe();
          this.previewLoading.set(false);
          this.setPreview(URL.createObjectURL(file));
          this.hasLogo.set(true);
          this.uploading.set(false);
          this.progress.set(null);
          this.messageService.add({ severity: 'success', summary: LOGO_UPDATED_MESSAGE });
        },
        error: (failure: unknown) => {
          this.uploading.set(false);
          this.progress.set(null);
          this.handleFailure(failure);
        },
      });
  }

  removeLogo(): void {
    if (this.busy()) {
      return;
    }
    this.error.set(null);
    this.removing.set(true);
    this.logoService
      .remove()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.removing.set(false);
          this.previewRequest?.unsubscribe();
          this.previewLoading.set(false);
          this.setPreview(null);
          this.hasLogo.set(false);
          this.messageService.add({ severity: 'success', summary: LOGO_REMOVED_MESSAGE });
        },
        error: (failure: unknown) => {
          this.removing.set(false);
          this.handleFailure(failure);
        },
      });
  }

  private loadPreview(metadata: OrganizationLogoMetadata | null): void {
    this.previewRequest?.unsubscribe();
    this.error.set(null);
    this.hasLogo.set(metadata !== null);
    if (metadata === null) {
      this.previewLoading.set(false);
      this.setPreview(null);
      return;
    }

    this.previewLoading.set(true);
    this.previewRequest = this.logoService.get().subscribe({
      next: (blob) => {
        this.previewLoading.set(false);
        this.setPreview(URL.createObjectURL(blob));
      },
      error: (failure: unknown) => {
        this.previewLoading.set(false);
        this.setPreview(null);
        if (isApiError(failure) && failure.kind === 'unauthorized') {
          this.unauthorized.emit();
        }
      },
    });
  }

  private handleFailure(failure: unknown): void {
    const apiError: ApiError | null = isApiError(failure) ? failure : null;
    if (apiError?.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    const fileMessage = apiError?.fieldErrors['file']?.[0];
    if (fileMessage === LOGO_SIZE_MESSAGE || fileMessage === LOGO_TYPE_MESSAGE) {
      this.error.set(fileMessage);
      return;
    }
    this.error.set(apiError?.message ?? 'An unexpected error occurred. Please try again.');
  }

  /** Swaps the object URL, revoking the previous one. */
  private setPreview(url: string | null): void {
    const previous = this.previewUrl();
    if (previous !== null) {
      URL.revokeObjectURL(previous);
    }
    this.previewUrl.set(url);
  }
}
