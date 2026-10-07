import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Subscription } from 'rxjs';

import { EvidenceType, TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  PHOTO_INVALID_MESSAGE,
  classifyMutationFailure,
  hasEvidence,
} from '../../utils/job-progress';
import { PhotoOpenRequest, VisitPhoto, VisitPhotos } from '../visit-photos/visit-photos';

export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
const PHOTO_TYPES: readonly string[] = ['image/jpeg', 'image/png'];

interface UploadState {
  /** Local pre-upload preview; revoked on success, failure, removal and destroy. */
  readonly previewUrl: string;
  readonly label: string;
  readonly percent: number;
}

/** BR-17 Photos & evidence section: thumbnails, Add photo (Before or After) with progress. */
@Component({
  selector: 'app-job-evidence',
  imports: [ButtonDirective, VisitPhotos],
  templateUrl: './job-evidence.html',
  styleUrl: './job-evidence.scss',
})
export class JobEvidence {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('file');
  private choice: EvidenceType = 'before';
  private subscription: Subscription | null = null;

  readonly visit = input.required<TechnicianVisitDetail>();
  readonly photos = input.required<readonly VisitPhoto[]>();
  readonly readOnly = input(false);
  readonly visitChange = output<TechnicianVisitDetail>();
  readonly reload = output<void>();
  readonly openPhoto = output<PhotoOpenRequest>();
  readonly announce = output<string>();

  readonly chooserOpen = signal(false);
  readonly upload = signal<UploadState | null>(null);
  readonly error = signal<string | null>(null);

  /** Request in flight; blocks repeats. */
  readonly pending = computed(() => this.upload() !== null);
  readonly complete = computed(
    () => hasEvidence(this.visit(), 'before') && hasEvidence(this.visit(), 'after'),
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.subscription?.unsubscribe();
      this.revoke();
    });
  }

  openChooser(): void {
    this.error.set(null);
    this.chooserOpen.set(true);
  }

  /** Opens the device picker (camera allowed) for the chosen type. */
  choose(type: EvidenceType): void {
    this.choice = type;
    this.chooserOpen.set(false);
    this.fileInput().nativeElement.click();
  }

  picked(input: HTMLInputElement): void {
    const file = input.files?.item(0) ?? null;
    input.value = '';
    if (file === null || this.pending()) {
      return;
    }
    if (!PHOTO_TYPES.includes(file.type) || file.size < 1 || file.size > MAX_PHOTO_BYTES) {
      this.error.set(PHOTO_INVALID_MESSAGE);
      return;
    }
    this.start(file, this.choice);
  }

  private start(file: File, type: EvidenceType): void {
    this.error.set(null);
    this.upload.set({
      previewUrl: URL.createObjectURL(file),
      label: type === 'before' ? 'Before' : 'After',
      percent: 0,
    });
    this.subscription = this.visits.uploadEvidence(this.visit().visitId, file, type).subscribe({
      next: (event) => {
        if (event.kind === 'progress') {
          this.upload.update((state) => state && { ...state, percent: event.percent });
          return;
        }
        this.revoke();
        this.announce.emit('Photo added.');
        this.visitChange.emit(event.visit);
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error, { invalid: PHOTO_INVALID_MESSAGE });
        this.revoke();
        this.error.set(failure.message);
        if (failure.reload) {
          this.reload.emit();
        }
      },
    });
  }

  private revoke(): void {
    const state = this.upload();
    if (state !== null) {
      URL.revokeObjectURL(state.previewUrl);
      this.upload.set(null);
    }
  }
}
