import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { SessionService } from '../../../../core/services/session.service';
import { VisitActions } from '../../components/visit-actions/visit-actions';
import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
} from '../../utils/technician-errors';
import {
  STATUS_LABELS,
  STATUS_SEVERITIES,
  addressLine,
  arrivalWindowText,
  formatShortDate,
  formatTimeRange,
  pluralLabel,
  priorityLabel,
} from '../../utils/technician-format';

export const NOT_AVAILABLE_MESSAGE = "This job isn't available.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this job. Try again.";
export const COMING_SOON_NOTE = 'Job execution is coming soon.';
export const TODAY_PATH = '/today';

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;

/** Minimal read-only job page (`/today/visits/:visitId`) that Design 8 extends. */
@Component({
  selector: 'app-visit-detail',
  imports: [RouterLink, ButtonDirective, Message, Skeleton, Tag, VisitActions],
  templateUrl: './visit-detail.html',
  styleUrl: './visit-detail.scss',
})
export class VisitDetail {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly visitId = inject(ActivatedRoute).snapshot.paramMap.get('visitId') ?? '';

  readonly todayPath = TODAY_PATH;
  readonly notAvailableMessage = NOT_AVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly comingSoonNote = COMING_SOON_NOTE;

  readonly state = signal<PageState>('loading');
  readonly visit = signal<TechnicianVisitDetail | null>(null);

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });
  readonly statusLabel = computed(() => STATUS_LABELS[this.visit()?.status ?? 'scheduled']);
  readonly statusSeverity = computed(() => STATUS_SEVERITIES[this.visit()?.status ?? 'scheduled']);
  readonly dateText = computed(() => formatShortDate(this.visit()?.date ?? ''));
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
  readonly materials = computed(() => {
    const count = this.visit()?.plannedMaterialsCount ?? 0;
    return count > 0 ? `${count} planned ${pluralLabel(count, 'material', 'materials')}` : null;
  });

  constructor() {
    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.visits.visit(this.visitId).subscribe({
      next: (visit) => {
        this.visit.set(visit);
        this.state.set('ready');
      },
      error: (error: unknown) => {
        const failure = classifyFailure(error);
        this.state.set(failure === 'failed' ? 'error' : failure);
      },
    });
  }
}
