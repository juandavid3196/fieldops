import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { SessionService } from '../../../../core/services/session.service';
import { PhotoViewer } from '../../components/photo-viewer/photo-viewer';
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

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;
type TravelAction = 'start-travel' | 'arrive';

/**
 * Job page (`/today/visits/:visitId`, Design 8). Read-only except Start travel and I've arrived,
 * which the backend authorizes (frontend hiding is UX only).
 */
@Component({
  selector: 'app-visit-detail',
  imports: [
    RouterLink,
    ButtonDirective,
    Dialog,
    Message,
    Skeleton,
    Tag,
    PhotoViewer,
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
  private readonly visitId = inject(ActivatedRoute).snapshot.paramMap.get('visitId') ?? '';
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
  readonly photos = computed<VisitPhoto[]>(
    () =>
      this.visit()?.assessment?.photos.map((photo) => ({
        id: photo.id,
        url: this.visits.assessmentPhotoUrl(this.visitId, photo.id),
      })) ?? [],
  );
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

  /** BR-14: one action at a time; success updates the page in place and announces it. */
  act(action: TravelAction): void {
    if (this.pending() !== null) {
      return;
    }
    this.pending.set(action);
    this.actionError.set(null);
    const request =
      action === 'start-travel'
        ? this.visits.startTravel(this.visitId)
        : this.visits.arrive(this.visitId);
    request.subscribe({
      next: (result) => {
        this.visit.set(result.visit);
        this.pending.set(null);
        this.announcement.set(announcementFor(result.visit));
      },
      error: (error: unknown) => {
        const failure = classifyTravelFailure(error);
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
