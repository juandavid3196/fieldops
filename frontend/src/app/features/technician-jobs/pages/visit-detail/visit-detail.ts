import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import {
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { SessionService } from '../../../../core/services/session.service';
import { JobProgress } from '../../components/job-progress/job-progress';
import { PhotoDeleteRequest, PhotoViewer } from '../../components/photo-viewer/photo-viewer';
import { ReportIssue } from '../../components/report-issue/report-issue';
import { VisitActions } from '../../components/visit-actions/visit-actions';
import {
  PhotoOpenRequest,
  VisitPhoto,
  VisitPhotos,
} from '../../components/visit-photos/visit-photos';
import { VisitStepper } from '../../components/visit-stepper/visit-stepper';
import { VisitTabs } from '../../components/visit-tabs/visit-tabs';
import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { classifyMutationFailure } from '../../utils/job-progress';
import {
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
  classifyTravelFailure,
} from '../../utils/technician-errors';
import {
  addressLine,
  arrivalWindowText,
  formatInstantShortDate,
  formatTimeRange,
  priorityLabel,
  telUrl,
} from '../../utils/technician-format';
import {
  actionBarFor,
  announcementFor,
  bannerFor,
  contactPreferenceText,
  customerTypeLabel,
  stepsFor,
} from '../../utils/visit-detail';

export const NOT_AVAILABLE_MESSAGE = "This job isn't available.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this job. Try again.";
export const TODAY_PATH = '/today';
export const JOB_IN_PROGRESS_TITLE = 'Job in progress';

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;
type TravelAction = 'start-travel' | 'arrive' | 'start-job';

/**
 * Job page (`/today/visits/:visitId`): Design 8 up to Start job, Design 9 (`app-job-progress`) while
 * the visit is `in_progress` or `paused`. Every response replaces the visit; the backend authorizes
 * each action (frontend hiding is UX only).
 */
@Component({
  selector: 'app-visit-detail',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    ButtonDirective,
    Dialog,
    Message,
    Skeleton,
    Tag,
    JobProgress,
    PhotoViewer,
    ReportIssue,
    VisitActions,
    VisitPhotos,
    VisitStepper,
    VisitTabs,
  ],
  templateUrl: './visit-detail.html',
  styleUrls: ['./visit-detail.scss', './visit-detail-bar.scss'],
})
export class VisitDetail {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute).snapshot;
  private readonly visitId = this.route.paramMap.get('visitId') ?? '';
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  /** Section requested by the link (`#job-section-tasks`); scrolled to once after the first load. */
  private pendingSection = this.route.fragment?.startsWith('job-section-')
    ? this.route.fragment
    : null;
  /** Triggers of the open overlays; the last one is focused again when its overlay closes. */
  private readonly focusStack: HTMLElement[] = [];

  readonly todayPath = TODAY_PATH;
  readonly notAvailableMessage = NOT_AVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;

  readonly state = signal<PageState>('loading');
  readonly visit = signal<TechnicianVisitDetail | null>(null);
  /** Action in flight; blocks repeats. */
  readonly pending = signal<TravelAction | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly announcement = signal('');
  readonly assessmentOpen = signal(false);
  readonly reportOpen = signal(false);
  readonly viewerIndex = signal<number | null>(null);
  readonly deleteTarget = signal<string | null>(null);
  readonly deleting = signal(false);
  readonly deleteError = signal<string | null>(null);

  /** BR-15: Design 9 for `in_progress` and `paused`. */
  readonly working = computed(() => {
    const status = this.visit()?.status;
    return status === 'in_progress' || status === 'paused';
  });
  readonly canDeletePhotos = computed(() => this.working() && this.visit()?.isPrimary === true);

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });
  readonly steps = computed(() => {
    const visit = this.visit();
    return visit === null ? [] : stepsFor(visit);
  });
  readonly banner = computed(() => {
    const visit = this.visit();
    return visit === null ? null : bannerFor(visit);
  });
  readonly bar = computed(() => {
    const visit = this.visit();
    return visit === null ? null : actionBarFor(visit);
  });
  readonly schedule = computed(() => {
    const visit = this.visit();
    return visit === null ? '' : formatTimeRange(visit.start, visit.end, visit.timezone);
  });
  readonly arrival = computed(() => {
    const visit = this.visit();
    return visit === null ? null : arrivalWindowText(visit, visit.timezone);
  });
  readonly address = computed(() => {
    const visit = this.visit();
    return visit === null ? '' : addressLine(visit.address);
  });
  readonly priority = computed(() => {
    const visit = this.visit();
    return visit === null ? null : priorityLabel(visit.priority);
  });
  readonly customerType = computed(() => {
    const visit = this.visit();
    return visit === null ? '' : customerTypeLabel(visit.customerType);
  });
  readonly contactPreference = computed(() =>
    contactPreferenceText(this.visit()?.access.contactPreference ?? null),
  );
  readonly hasAccessLines = computed(() => {
    const access = this.visit()?.access;
    return (
      access !== undefined &&
      ((access.instructions?.trim() ?? '') !== '' ||
        this.contactPreference() !== null ||
        access.activeDamage)
    );
  });
  /** Job photos while working (Before/After labels); otherwise the approved assessment photos. */
  readonly photos = computed<VisitPhoto[]>(() => {
    const visit = this.visit();
    if (this.working()) {
      return (
        visit?.evidence.map((item) => ({
          id: item.id,
          url: this.visits.evidencePhotoUrl(this.visitId, item.id),
          label: item.type === 'before' ? 'Before' : 'After',
        })) ?? []
      );
    }
    return (
      visit?.assessment?.photos.map((photo) => ({
        id: photo.id,
        url: this.visits.assessmentPhotoUrl(this.visitId, photo.id),
      })) ?? []
    );
  });
  readonly completedText = computed(() => {
    const visit = this.visit();
    const assessment = visit?.assessment;
    return visit && assessment
      ? `Completed ${formatInstantShortDate(assessment.completedAt, visit.timezone)}`
      : '';
  });
  readonly officeHref = computed(() => {
    const phone = this.visit()?.officePhone?.trim() ?? '';
    return phone === '' ? null : telUrl(phone);
  });

  constructor() {
    // The top bar reads "Job in progress" while Design 9 shows; a status change needs no navigation.
    effect(() => this.visits.setPageTitle(this.working() ? JOB_IN_PROGRESS_TITLE : null));
    this.destroyRef.onDestroy(() => this.visits.setPageTitle(null));
    // The bottom-nav line grows with the stepper's completed steps (Scheduled, On the way, …).
    effect(() => {
      const steps = this.steps();
      const done = steps.filter((step) => step.state === 'done').length;
      this.visits.setJobProgress(steps.length === 0 ? null : (done / steps.length) * 100);
    });
    this.destroyRef.onDestroy(() => this.visits.setJobProgress(null));
    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.load();
  }

  /** Initial load and Retry show the skeleton; a reload after a conflict keeps the page. */
  load(silent = false): void {
    if (!silent) {
      this.state.set('loading');
    }
    this.visits.visit(this.visitId).subscribe({
      next: (visit) => {
        this.visit.set(visit);
        this.state.set('ready');
        this.scrollToSection();
      },
      error: (error: unknown) => {
        if (silent) {
          return;
        }
        const failure = classifyFailure(error);
        this.state.set(failure === 'failed' ? 'error' : failure);
      },
    });
  }

  private scrollToSection(): void {
    const id = this.pendingSection;
    this.pendingSection = null;
    if (id !== null && this.working()) {
      afterNextRender(() => this.document.getElementById(id)?.scrollIntoView(), {
        injector: this.injector,
      });
    }
  }

  /** BR-14 / BR-15: one action at a time; success updates the page in place and announces it. */
  act(action: TravelAction): void {
    if (this.pending() !== null) {
      return;
    }
    this.pending.set(action);
    this.actionError.set(null);
    const request =
      action === 'start-travel'
        ? this.visits.startTravel(this.visitId)
        : action === 'arrive'
          ? this.visits.arrive(this.visitId)
          : this.visits.startJob(this.visitId);
    request.subscribe({
      next: (result) => {
        this.visit.set(result.visit);
        this.pending.set(null);
        this.announcement.set(
          action === 'start-job' ? 'Job started.' : announcementFor(result.visit),
        );
      },
      error: (error: unknown) => {
        const failure = classifyTravelFailure(
          error,
          action === 'start-job' ? 'start-job' : 'travel',
        );
        this.pending.set(null);
        this.actionError.set(failure.message);
        if (failure.reload) {
          this.load(true);
        }
      },
    });
  }

  openPhoto(request: PhotoOpenRequest): void {
    this.focusStack.push(request.trigger);
    this.viewerIndex.set(request.index);
  }

  openAssessment(trigger: HTMLElement): void {
    this.focusStack.push(trigger);
    this.assessmentOpen.set(true);
  }

  openReport(trigger: HTMLElement): void {
    this.focusStack.push(trigger);
    this.reportOpen.set(true);
  }

  /** Viewer Delete photo: asks for confirmation on top of the viewer. */
  requestDelete(request: PhotoDeleteRequest): void {
    this.focusStack.push(request.trigger);
    this.deleteError.set(null);
    this.deleteTarget.set(request.id);
  }

  cancelDelete(): void {
    if (this.deleteTarget() !== null && !this.deleting()) {
      this.deleteTarget.set(null);
      this.restoreFocus();
    }
  }

  confirmDelete(): void {
    const id = this.deleteTarget();
    if (id === null || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.deleteError.set(null);
    this.visits.deleteEvidence(this.visitId, id).subscribe({
      next: (visit) => {
        this.visit.set(visit);
        this.deleting.set(false);
        this.deleteTarget.set(null);
        this.restoreFocus();
        this.announcement.set('Photo deleted.');
        const count = this.photos().length;
        const index = this.viewerIndex();
        if (count === 0) {
          this.viewerIndex.set(null);
          this.restoreFocus();
        } else if (index !== null && index >= count) {
          this.viewerIndex.set(count - 1);
        }
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error);
        this.deleting.set(false);
        this.deleteError.set(failure.message);
        if (failure.reload) {
          this.load(true);
        }
      },
    });
  }

  closeAssessment(): void {
    if (this.assessmentOpen()) {
      this.assessmentOpen.set(false);
      this.restoreFocus();
    }
  }

  closeReport(): void {
    if (this.reportOpen()) {
      this.reportOpen.set(false);
      this.restoreFocus();
    }
  }

  /** Viewer hide: the viewer already cleared its index; focus returns to the thumbnail. */
  viewerClosed(): void {
    this.restoreFocus();
  }

  private restoreFocus(): void {
    const target = this.focusStack.pop();
    setTimeout(() => target?.focus());
  }
}
