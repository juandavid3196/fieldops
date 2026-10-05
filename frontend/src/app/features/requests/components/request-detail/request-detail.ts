import { Component, computed, input, model, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MenuItem } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';

import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { formatDate, formatTime } from '../../../customers/utils/customer-format';
import {
  RequestAction,
  RequestActionId,
  RequestDetail,
  SOURCE_LABELS,
  STATUS_LABELS,
  URGENCY_LABELS,
  canEditRequest,
  footerActions,
  menuActions,
} from '../../models/requests.model';
import { activityTime, addressLine, preferredVisit } from '../../utils/requests-format';
import { RequestAttachments } from '../request-attachments/request-attachments';

export type DetailState = 'loading' | 'ready' | 'not-found' | 'error';

export const NOT_AVAILABLE_MESSAGE = "This request isn't available.";
export const DETAIL_ERROR_MESSAGE = "We couldn't load this request.";
export const NOTE_PLACEHOLDER = 'Add an internal note (not visible to customer)...';

/**
 * Request detail panel (BR-08, BR-03, BR-19) projected into the shared drawer shell: docked beside
 * the board from 1440px, overlay drawer from 768px, full screen with a back control below.
 * Presentational: the page owns the data, the actions and the URL.
 */
@Component({
  selector: 'app-request-detail',
  imports: [
    FormsModule,
    ButtonDirective,
    Menu,
    Message,
    Skeleton,
    Textarea,
    DrawerShell,
    RequestAttachments,
  ],
  templateUrl: './request-detail.html',
  styleUrl: './request-detail.scss',
})
export class RequestDetailPanel {
  readonly open = input.required<boolean>();
  readonly state = input.required<DetailState>();
  readonly detail = input<RequestDetail | null>(null);
  readonly timezone = input('UTC');
  readonly canManage = input(false);
  /** A mutation is in flight: footer, menu and inputs are disabled. */
  readonly busy = input(false);
  readonly noteText = model('');
  readonly noteError = input<string | null>(null);
  readonly uploadErrors = input<readonly string[]>([]);

  readonly closeRequested = output<void>();
  readonly retry = output<void>();
  readonly action = output<RequestActionId>();
  readonly noteSubmitted = output<string>();
  readonly filesSelected = output<File[]>();

  private readonly menu = viewChild(Menu);

  readonly titleId = 'request-detail-title';
  readonly notAvailable = NOT_AVAILABLE_MESSAGE;
  readonly loadError = DETAIL_ERROR_MESSAGE;
  readonly notePlaceholder = NOTE_PLACEHOLDER;
  readonly urgencyLabels = URGENCY_LABELS;
  readonly statusLabels = STATUS_LABELS;
  readonly sourceLabels = SOURCE_LABELS;

  readonly title = computed(() => this.detail()?.title ?? 'Request');
  readonly footer = computed<RequestAction[]>(() => {
    const detail = this.detail();
    return detail === null ? [] : footerActions(detail.status, this.canManage());
  });
  readonly menuModel = computed<MenuItem[]>(() => {
    const detail = this.detail();
    return (detail === null ? [] : menuActions(detail.status, this.canManage())).map((item) => ({
      label: item.label,
      disabled: this.busy(),
      command: () => this.action.emit(item.id),
    }));
  });
  readonly editable = computed(() => {
    const detail = this.detail();
    return detail !== null && canEditRequest(detail.status, this.canManage());
  });
  readonly localNoteError = computed(() =>
    this.noteText().length > 2000 ? 'Use 2000 characters or fewer.' : null,
  );

  readonly address = computed(() => addressLine(this.detail()?.serviceAddress ?? null));
  readonly visit = computed(() => preferredVisit(this.detail()?.availability ?? null));
  readonly assessmentLine = computed(() => {
    const assessment = this.detail()?.assessment;
    if (assessment === null || assessment === undefined) {
      return null;
    }
    const zone = this.timezone();
    return {
      date: formatDate(assessment.start, zone),
      time: `${formatTime(assessment.start, zone)} – ${formatTime(assessment.end, zone)}`,
      technician: assessment.technician?.name ?? null,
      purpose: assessment.purpose,
      instructions: assessment.internalInstructions,
    };
  });

  readonly stamp = (iso: string): string => activityTime(iso, this.timezone());

  openMenu(event: Event): void {
    this.menu()?.toggle(event);
  }

  submitNote(): void {
    const body = this.noteText().trim();
    if (body.length === 0 || this.localNoteError() !== null) {
      return;
    }
    this.noteSubmitted.emit(body);
  }
}
