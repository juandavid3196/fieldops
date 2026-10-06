import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Textarea } from 'primeng/textarea';

import { discardChangesConfirmation } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { CompleteAssessmentBody } from '../../models/requests.model';
import { fileSize } from '../../utils/requests-format';

export const DIAGNOSIS_REQUIRED = 'Enter the diagnosis.';
export const DIAGNOSIS_TOO_LONG = 'Diagnosis must be 2000 characters or fewer.';
export const SCOPE_TOO_LONG = 'Recommended scope must be 2000 characters or fewer.';
export const PHOTO_COUNT_MESSAGE = 'Add up to 6 photos.';
export const PHOTO_TYPE_MESSAGE = 'Photos must be JPG or PNG files of 10 MB or less.';
export const MAX_PHOTOS = 6;
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
export const MAX_TOTAL_BYTES = 25 * 1024 * 1024;
const PHOTO_EXTENSION = /\.(jpe?g|png)$/i;

/** BR-01/BR-02 client checks of the findings; the backend re-validates and detects the content type. */
export function assessmentErrors(
  diagnosis: string,
  scope: string,
  photos: readonly File[],
): Record<string, string> {
  const errors: Record<string, string> = {};
  const text = diagnosis.trim();
  if (text.length === 0) {
    errors['diagnosis'] = DIAGNOSIS_REQUIRED;
  } else if (text.length > 2000) {
    errors['diagnosis'] = DIAGNOSIS_TOO_LONG;
  }
  if (scope.trim().length > 2000) {
    errors['recommendedScope'] = SCOPE_TOO_LONG;
  }
  if (photos.length > MAX_PHOTOS) {
    errors['photos'] = PHOTO_COUNT_MESSAGE;
  } else if (
    photos.some(
      (file) => file.size < 1 || file.size > MAX_PHOTO_BYTES || !PHOTO_EXTENSION.test(file.name),
    ) ||
    photos.reduce((total, file) => total + file.size, 0) > MAX_TOTAL_BYTES
  ) {
    errors['photos'] = PHOTO_TYPE_MESSAGE;
  }
  return errors;
}

/**
 * Complete assessment dialog (BR-01): diagnosis, recommended scope and 0-6 photos. Collects and
 * validates only; the page runs the multipart request and owns `409` and failure handling.
 * Closing with edits asks through the shared discard-changes dialog.
 */
@Component({
  selector: 'app-complete-assessment-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, SpinnerIcon, Textarea, FormField],
  templateUrl: './complete-assessment-dialog.html',
  styleUrl: './complete-assessment-dialog.scss',
})
export class CompleteAssessmentDialog {
  private readonly confirmation = inject(ConfirmationService);

  readonly open = input.required<boolean>();
  readonly submitting = input(false);
  /** `400` field errors keyed `diagnosis`, `recommendedScope`, `photos`. */
  readonly fieldErrors = input<Readonly<Record<string, string>>>({});

  readonly dismissed = output<void>();
  readonly submitted = output<CompleteAssessmentBody>();

  readonly diagnosis = signal('');
  readonly scope = signal('');
  readonly photos = signal<readonly File[]>([]);
  private readonly localErrors = signal<Readonly<Record<string, string>>>({});

  readonly size = fileSize;
  readonly accept = '.jpg,.jpeg,.png';
  readonly dirty = computed(
    () => this.diagnosis().trim() !== '' || this.scope().trim() !== '' || this.photos().length > 0,
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.diagnosis.set('');
          this.scope.set('');
          this.photos.set([]);
          this.localErrors.set({});
        });
      }
    });
  }

  error(field: string): string | null {
    return this.localErrors()[field] ?? this.fieldErrors()[field] ?? null;
  }

  describedBy(field: string): string | null {
    return this.error(field) === null ? null : `complete-assessment-${field}-error`;
  }

  private clearPhotoError(): void {
    this.localErrors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([field]) => field !== 'photos')),
    );
  }

  addPhotos(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    input.value = '';
    this.photos.update((current) => [...current, ...picked]);
    this.clearPhotoError();
  }

  removePhoto(index: number): void {
    this.photos.update((current) => current.filter((_, position) => position !== index));
    this.clearPhotoError();
  }

  /** Cancel, Escape: asks first when the fields changed. */
  requestClose(): void {
    if (this.submitting()) {
      return;
    }
    if (!this.dirty()) {
      this.dismissed.emit();
      return;
    }
    this.confirmation.confirm(
      discardChangesConfirmation({
        subject: 'this assessment',
        accept: () => this.dismissed.emit(),
      }),
    );
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }
    const errors = assessmentErrors(this.diagnosis(), this.scope(), this.photos());
    this.localErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    const scope = this.scope().trim();
    this.submitted.emit({
      diagnosis: this.diagnosis().trim(),
      recommendedScope: scope.length > 0 ? scope : null,
      photos: this.photos(),
    });
  }
}
