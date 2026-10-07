import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Tab, TabList, TabPanel, TabPanels, Tabs } from 'primeng/tabs';

import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  NOTES_SAVE_ERROR_MESSAGE,
  PAUSE_INVALID_MESSAGE,
  classifyMutationFailure,
  formatLabor,
  laborSeconds,
  reviewReady,
} from '../../utils/job-progress';
import { formatTime } from '../../utils/technician-format';
import { JobEvidence } from '../job-evidence/job-evidence';
import { JobMaterials } from '../job-materials/job-materials';
import { JobNotes } from '../job-notes/job-notes';
import { JobTasks } from '../job-tasks/job-tasks';
import { JobTime } from '../job-time/job-time';
import { PhotoOpenRequest, VisitPhoto } from '../visit-photos/visit-photos';

export const TICK_MS = 30_000;
export const NOTES_INVALID_MESSAGE = 'Notes can be up to 4000 characters.';
export const REVIEW_READY_TEXT = 'Next: customer review and signature';
export const REVIEW_BLOCKED_TEXT = 'Complete required tasks and add before and after photos';
export const NOT_PRIMARY_JOB_TEXT = 'The primary technician manages this job.';

/** Design 9 body for `in_progress` and `paused` visits: banner, tabs, time summary and action bar. */
@Component({
  selector: 'app-job-progress',
  imports: [
    RouterLink,
    ButtonDirective,
    Tabs,
    TabList,
    Tab,
    TabPanels,
    TabPanel,
    JobTasks,
    JobMaterials,
    JobEvidence,
    JobNotes,
    JobTime,
  ],
  templateUrl: './job-progress.html',
  styleUrls: ['./job-progress.scss', './job-progress-bar.scss'],
})
export class JobProgress {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly router = inject(Router);
  private exitRequested = false;
  private readonly tasks = viewChild(JobTasks);
  private readonly materials = viewChild(JobMaterials);
  private readonly evidence = viewChild(JobEvidence);

  readonly visit = input.required<TechnicianVisitDetail>();
  /** Job photos for thumbnails and the viewer (owned by the page). */
  readonly photos = input.required<readonly VisitPhoto[]>();
  readonly visitChange = output<TechnicianVisitDetail>();
  readonly reload = output<void>();
  readonly openReport = output<HTMLElement>();
  readonly openPhoto = output<PhotoOpenRequest>();

  readonly todayPath = '/today';
  readonly readyText = REVIEW_READY_TEXT;
  readonly blockedText = REVIEW_BLOCKED_TEXT;
  readonly notPrimaryText = NOT_PRIMARY_JOB_TEXT;

  /** Client clock for the live labor and break time. */
  readonly now = signal(Date.now());
  readonly announcement = signal('');
  readonly transition = signal<'pause' | 'resume' | null>(null);
  readonly transitionError = signal<string | null>(null);

  readonly notesDraft = linkedSignal({
    source: () => this.visit().technicianNotes ?? '',
    computation: (notes) => notes,
  });
  readonly notesPending = signal(false);
  readonly notesSaved = signal(false);
  readonly notesError = signal<string | null>(null);
  readonly exitError = signal<string | null>(null);

  readonly readOnly = computed(() => !this.visit().isPrimary);
  readonly paused = computed(() => this.visit().status === 'paused');
  readonly laborText = computed(() => formatLabor(laborSeconds(this.visit().time, this.now())));
  readonly startedText = computed(() => {
    const visit = this.visit();
    return visit.actualStartedAt === null
      ? null
      : `Started ${formatTime(visit.actualStartedAt, visit.timezone)}`;
  });
  readonly ready = computed(() => reviewReady(this.visit()));
  readonly notesDirty = computed(
    () => this.notesDraft().trim() !== (this.visit().technicianNotes ?? ''),
  );
  readonly busy = computed(
    () =>
      this.transition() !== null ||
      this.notesPending() ||
      (this.tasks()?.pending() ?? null) !== null ||
      (this.materials()?.pending() ?? null) !== null ||
      (this.evidence()?.pending() ?? false),
  );

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    // A fresh response restarts the clock so the active entry is measured against "now".
    effect(() => {
      this.visit();
      untracked(() => this.now.set(Date.now()));
    });
  }

  /** Pause or Resume; the response replaces the visit. */
  toggle(): void {
    if (this.transition() !== null) {
      return;
    }
    const action = this.paused() ? 'resume' : 'pause';
    const visitId = this.visit().visitId;
    this.transition.set(action);
    this.transitionError.set(null);
    (action === 'pause' ? this.visits.pause(visitId) : this.visits.resume(visitId)).subscribe({
      next: (result) => {
        this.transition.set(null);
        this.announcement.set(action === 'pause' ? 'Job paused.' : 'Job resumed.');
        this.visitChange.emit(result.visit);
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error, { conflict: PAUSE_INVALID_MESSAGE });
        this.transition.set(null);
        this.transitionError.set(failure.message);
        if (failure.reload) {
          this.reload.emit();
        }
      },
    });
  }

  /** Notes are saved when the field loses focus and the text changed. */
  notesBlurred(): void {
    if (!this.readOnly() && this.notesDirty()) {
      this.persistNotes();
    }
  }

  /** Saves pending notes, then opens Today; a failure keeps the page and the text. */
  saveAndExit(): void {
    this.exitError.set(null);
    this.exitRequested = true;
    if (this.notesPending()) {
      return;
    }
    if (!this.readOnly() && this.notesDirty()) {
      this.persistNotes();
    } else {
      this.finishExit();
    }
  }

  review(): void {
    if (this.ready() && !this.busy()) {
      void this.router.navigate(['/today/visits', this.visit().visitId, 'review']);
    }
  }

  private persistNotes(): void {
    this.notesPending.set(true);
    this.notesSaved.set(false);
    this.notesError.set(null);
    this.visits.saveNotes(this.visit().visitId, this.notesDraft().trim()).subscribe({
      next: (visit) => {
        this.notesPending.set(false);
        this.notesSaved.set(true);
        this.visitChange.emit(visit);
        if (this.exitRequested) {
          this.finishExit();
        }
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error, { invalid: NOTES_INVALID_MESSAGE });
        this.notesPending.set(false);
        this.notesError.set(failure.message);
        if (this.exitRequested) {
          this.exitRequested = false;
          this.exitError.set(NOTES_SAVE_ERROR_MESSAGE);
        } else if (failure.reload) {
          this.reload.emit();
        }
      },
    });
  }

  private finishExit(): void {
    this.exitRequested = false;
    void this.router.navigateByUrl(this.todayPath);
  }
}
