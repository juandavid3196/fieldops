import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { ProgressBar } from 'primeng/progressbar';
import { Skeleton } from 'primeng/skeleton';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Toast } from 'primeng/toast';
import { Observable } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { UserAvatar } from '../../../../shared/components/user-avatar/user-avatar';
import { fullNameOf, initialsOf } from '../../../../shared/utils/initials';
import { AccountProfile } from '../../models/account.model';
import { AccountService } from '../../services/account.service';
import {
  PHOTO_ACCEPT,
  PHOTO_HELPER,
  ProfileErrors,
  ProfileField,
  validatePhotoFile,
  validateProfile,
} from '../../utils/account-validation';

export const PROFILE_LOAD_ERROR = "We couldn't load your profile. Try again.";
export const PROFILE_SAVE_ERROR = "We couldn't save your changes. Try again.";
export const PHOTO_ERROR = "We couldn't update your photo. Try again.";

const FIELDS: readonly ProfileField[] = ['firstName', 'lastName', 'phone'];

type ProfileValues = Record<ProfileField, string>;

/** My profile (FR-06..FR-08): personal information, read-only membership and the photo. */
@Component({
  selector: 'app-profile',
  imports: [
    ReactiveFormsModule,
    ButtonDirective,
    InputText,
    Message,
    ProgressBar,
    Skeleton,
    SpinnerIcon,
    Toast,
    DiscardChangesDialog,
    UserAvatar,
  ],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
  providers: [MessageService, ConfirmationService],
})
export class Profile {
  private readonly accountService = inject(AccountService);
  private readonly sessionService = inject(SessionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly loadError = PROFILE_LOAD_ERROR;
  readonly saveErrorText = PROFILE_SAVE_ERROR;
  readonly photoHelper = PHOTO_HELPER;
  readonly photoAccept = PHOTO_ACCEPT;

  readonly form = new FormGroup({
    firstName: new FormControl('', { nonNullable: true }),
    lastName: new FormControl('', { nonNullable: true }),
    phone: new FormControl('', { nonNullable: true }),
  });

  readonly profile = signal<AccountProfile | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly submitting = signal(false);
  readonly fieldErrors = signal<ProfileErrors>({});
  readonly saveFailed = signal(false);

  readonly hasPhoto = signal(false);
  readonly uploading = signal(false);
  readonly removing = signal(false);
  readonly progress = signal<number | null>(null);
  readonly photoError = signal<string | null>(null);
  readonly photoBusy = computed(() => this.uploading() || this.removing());

  readonly fullName = computed(() => fullNameOf(this.profile() ?? undefined));
  readonly initials = computed(() => initialsOf(this.fullName()));
  readonly photoUpdatedAt = computed(
    () => this.sessionService.session()?.user.photoUpdatedAt ?? null,
  );

  private baseline: ProfileValues = { firstName: '', lastName: '', phone: '' };
  private sessionExpired = false;

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.accountService
      .getProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (profile) => {
          this.applyProfile(profile);
          this.loading.set(false);
        },
        error: (failure: unknown) => {
          this.loading.set(false);
          if (!this.handleUnauthorized(failure)) {
            this.loadFailed.set(true);
          }
        },
      });
  }

  /** Consulted by `accountUnsavedChangesGuard` on route leave (FR-12). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired || !this.isDirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      const settle = (leave: boolean): void => {
        subscriber.next(leave);
        subscriber.complete();
      };
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'your profile',
          accept: () => settle(true),
          reject: () => settle(false),
        }),
      );
    });
  }

  /** FR-07: restores the last saved values and clears the field errors. */
  cancel(): void {
    this.form.reset(this.baseline);
    this.fieldErrors.set({});
    this.saveFailed.set(false);
  }

  save(): void {
    if (this.submitting()) {
      return;
    }
    this.saveFailed.set(false);

    const value = this.form.getRawValue();
    const errors = validateProfile(value);
    this.fieldErrors.set(errors);
    const invalid = FIELDS.find((field) => errors[field] !== undefined);
    if (invalid !== undefined) {
      this.host.querySelector<HTMLElement>(`#profile-${invalid}`)?.focus();
      return;
    }

    const phone = value.phone.trim();
    this.submitting.set(true);
    this.accountService
      .updateProfile({
        firstName: value.firstName.trim(),
        lastName: value.lastName.trim(),
        phone: phone === '' ? null : phone,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (profile) => {
          this.submitting.set(false);
          this.applyProfile(profile);
          this.messageService.add({ severity: 'success', summary: 'Profile updated' });
          this.sessionService
            .refreshCurrent()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe();
        },
        error: (failure: unknown) => {
          this.submitting.set(false);
          this.handleSaveFailure(failure);
        },
      });
  }

  /** FR-08: validates first, then uploads at once; never touches the form state. */
  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    input.value = '';
    if (file === null || this.photoBusy()) {
      return;
    }

    const message = await validatePhotoFile(file);
    if (message !== null) {
      this.photoError.set(message);
      return;
    }

    this.photoError.set(null);
    this.uploading.set(true);
    this.progress.set(0);
    this.accountService
      .uploadPhoto(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (uploadEvent) => {
          if (uploadEvent.kind === 'progress') {
            this.progress.set(uploadEvent.percent);
            return;
          }
          this.uploading.set(false);
          this.progress.set(null);
          this.hasPhoto.set(true);
          this.setSessionPhoto(uploadEvent.photo.updatedAt);
          this.messageService.add({ severity: 'success', summary: 'Photo updated' });
        },
        error: (failure: unknown) => {
          this.uploading.set(false);
          this.progress.set(null);
          this.handlePhotoFailure(failure);
        },
      });
  }

  removePhoto(): void {
    if (this.photoBusy()) {
      return;
    }
    this.photoError.set(null);
    this.removing.set(true);
    this.accountService
      .removePhoto()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.removing.set(false);
          this.hasPhoto.set(false);
          this.setSessionPhoto(null);
          this.messageService.add({ severity: 'success', summary: 'Photo removed' });
        },
        error: (failure: unknown) => {
          this.removing.set(false);
          this.handlePhotoFailure(failure);
        },
      });
  }

  private applyProfile(profile: AccountProfile): void {
    this.profile.set(profile);
    this.baseline = {
      firstName: profile.firstName,
      lastName: profile.lastName,
      phone: profile.phone ?? '',
    };
    this.form.reset(this.baseline);
    this.fieldErrors.set({});
    this.hasPhoto.set(profile.photo !== null);
  }

  private isDirty(): boolean {
    const value = this.form.getRawValue();
    return FIELDS.some((field) => value[field] !== this.baseline[field]);
  }

  /** Every avatar reads the session, so the photo response updates the session in place. */
  private setSessionPhoto(updatedAt: string | null): void {
    const session = this.sessionService.session();
    if (session !== null) {
      this.sessionService.setSession({
        ...session,
        user: { ...session.user, photoUpdatedAt: updatedAt },
      });
    }
  }

  private handleSaveFailure(failure: unknown): void {
    if (this.handleUnauthorized(failure)) {
      return;
    }
    const known: ProfileErrors = {};
    if (isApiError(failure)) {
      for (const field of FIELDS) {
        const message = failure.fieldErrors[field]?.[0];
        if (message !== undefined) {
          known[field] = message;
        }
      }
    }
    if (Object.keys(known).length > 0) {
      this.fieldErrors.set(known);
    } else {
      this.saveFailed.set(true);
    }
  }

  private handlePhotoFailure(failure: unknown): void {
    if (this.handleUnauthorized(failure)) {
      return;
    }
    const fileMessage = isApiError(failure) ? failure.fieldErrors['file']?.[0] : undefined;
    this.photoError.set(fileMessage ?? PHOTO_ERROR);
  }

  /** `401`: the session is gone; leave without the discard dialog. */
  private handleUnauthorized(failure: unknown): boolean {
    if (isApiError(failure) && failure.status === 401) {
      this.sessionExpired = true;
      void this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
      return true;
    }
    return false;
  }
}
