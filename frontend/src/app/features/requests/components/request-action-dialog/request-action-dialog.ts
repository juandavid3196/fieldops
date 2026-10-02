import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';

import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  AssessmentBody,
  RequestActionId,
  RequestDetail,
  RequestOptions,
  URGENCY_LABELS,
  Urgency,
} from '../../models/requests.model';
import { toLocalInput } from '../../utils/requests-format';

export type DialogKind = Extract<
  RequestActionId,
  | 'assign'
  | 'change-priority'
  | 'set-branch'
  | 'request-information'
  | 'log-response'
  | 'schedule-assessment'
  | 'reschedule'
  | 'cancel-request'
>;

export type DialogSubmit =
  | { readonly kind: 'assign'; readonly assigneeUserId: string | null }
  | { readonly kind: 'change-priority'; readonly urgency: Urgency }
  | { readonly kind: 'set-branch'; readonly branchId: string }
  | { readonly kind: 'request-information' | 'log-response'; readonly body: string }
  | { readonly kind: 'schedule-assessment' | 'reschedule'; readonly body: AssessmentBody }
  | { readonly kind: 'cancel-request'; readonly reason: string };

export const DIALOG_KINDS: readonly RequestActionId[] = [
  'assign',
  'change-priority',
  'set-branch',
  'request-information',
  'log-response',
  'schedule-assessment',
  'reschedule',
  'cancel-request',
];

export function isDialogKind(id: RequestActionId): id is DialogKind {
  return DIALOG_KINDS.includes(id);
}

interface Option {
  readonly code: string;
  readonly label: string;
}

const TITLES: Readonly<Record<DialogKind, string>> = {
  assign: 'Assign request',
  'change-priority': 'Change priority',
  'set-branch': 'Set branch',
  'request-information': 'Request information',
  'log-response': 'Log customer response',
  'schedule-assessment': 'Schedule assessment',
  reschedule: 'Reschedule assessment',
  'cancel-request': 'Cancel request',
};

const SUBMIT_LABELS: Readonly<Record<DialogKind, string>> = {
  assign: 'Assign',
  'change-priority': 'Save priority',
  'set-branch': 'Set branch',
  'request-information': 'Send request',
  'log-response': 'Log response',
  'schedule-assessment': 'Schedule',
  reschedule: 'Reschedule',
  'cancel-request': 'Cancel request',
};

const UNASSIGNED = 'unassigned';
const NO_TECHNICIAN = 'none';
const REQUIRED = 'This field is required.';

/**
 * Action dialogs of the request panel (BR-09 to BR-15): one form per kind with client checks and
 * the page's `400` field errors inline. The page runs the mutation and owns 409 and failure
 * handling, so this component only collects and validates input.
 */
@Component({
  selector: 'app-request-action-dialog',
  imports: [
    NgTemplateOutlet,
    FormsModule,
    ButtonDirective,
    Dialog,
    InputText,
    Select,
    SpinnerIcon,
    Textarea,
    FormField,
  ],
  templateUrl: './request-action-dialog.html',
  styleUrl: './request-action-dialog.scss',
})
export class RequestActionDialog {
  readonly kind = input.required<DialogKind | null>();
  readonly detail = input<RequestDetail | null>(null);
  readonly options = input<RequestOptions | null>(null);
  readonly timezone = input('UTC');
  readonly submitting = input(false);
  /** `400` field errors keyed by camelCase field (`assigneeUserId`, `branchId`, `body`, ...). */
  readonly fieldErrors = input<Readonly<Record<string, string>>>({});

  readonly dismissed = output<void>();
  readonly submitted = output<DialogSubmit>();

  readonly assignee = signal(UNASSIGNED);
  readonly urgency = signal<Urgency>('standard');
  readonly branchId = signal('');
  readonly body = signal('');
  readonly start = signal('');
  readonly endTime = signal('');
  readonly technicianId = signal(NO_TECHNICIAN);
  private readonly localErrors = signal<Readonly<Record<string, string>>>({});

  readonly priorityOptions: Option[] = (Object.keys(URGENCY_LABELS) as Urgency[]).map((code) => ({
    code,
    label: URGENCY_LABELS[code],
  }));

  readonly visible = computed(() => this.kind() !== null);
  readonly title = computed(() => {
    const kind = this.kind();
    if (kind === null) {
      return '';
    }
    return kind === 'cancel-request'
      ? `Cancel ${this.detail()?.number ?? 'request'}?`
      : TITLES[kind];
  });
  readonly submitLabel = computed(() => {
    const kind = this.kind();
    return kind === null ? '' : SUBMIT_LABELS[kind];
  });
  readonly isAssessment = computed(
    () => this.kind() === 'schedule-assessment' || this.kind() === 'reschedule',
  );
  readonly isMessage = computed(
    () => this.kind() === 'request-information' || this.kind() === 'log-response',
  );

  readonly assigneeOptions = computed<Option[]>(() => {
    const branchId = this.detail()?.branch?.id ?? null;
    const eligible = (this.options()?.assignees ?? []).filter(
      (member) =>
        branchId === null || member.branchIds === 'all' || member.branchIds.includes(branchId),
    );
    return [
      { code: UNASSIGNED, label: 'Unassigned' },
      ...eligible.map((member) => ({ code: member.userId, label: member.name })),
    ];
  });
  readonly branchOptions = computed<Option[]>(() =>
    (this.options()?.branches ?? []).map((branch) => ({ code: branch.id, label: branch.name })),
  );
  /** A request with no branch needs one to schedule (BR-15). */
  readonly needsBranch = computed(
    () => this.kind() === 'schedule-assessment' && this.detail()?.branch === null,
  );
  readonly technicianOptions = computed<Option[]>(() => {
    const branchId = this.detail()?.branch?.id ?? (this.branchId() || null);
    return [
      { code: NO_TECHNICIAN, label: 'No technician' },
      ...(this.options()?.technicians ?? [])
        .filter((technician) => technician.branchId === branchId)
        .map((technician) => ({ code: technician.id, label: technician.name })),
    ];
  });
  readonly messageLabel = computed(() =>
    this.kind() === 'request-information' ? 'Message to the customer' : 'Customer response',
  );

  constructor() {
    effect(() => {
      const kind = this.kind();
      if (kind !== null) {
        untracked(() => this.reset(kind));
      }
    });
  }

  private reset(kind: DialogKind): void {
    const detail = this.detail();
    const zone = this.timezone();
    this.localErrors.set({});
    this.body.set('');
    this.assignee.set(detail?.assignee?.userId ?? UNASSIGNED);
    this.urgency.set(detail?.urgency ?? 'standard');
    this.branchId.set(detail?.branch?.id ?? '');
    this.technicianId.set(NO_TECHNICIAN);
    this.start.set('');
    this.endTime.set('');
    if (kind === 'reschedule' && detail?.assessment) {
      const start = toLocalInput(detail.assessment.start, zone);
      this.start.set(start);
      this.endTime.set(toLocalInput(detail.assessment.end, zone).slice(11));
      this.technicianId.set(detail.assessment.technician?.id ?? NO_TECHNICIAN);
    }
  }

  error(field: string): string | null {
    return this.localErrors()[field] ?? this.fieldErrors()[field] ?? null;
  }

  describedBy(field: string): string | null {
    return this.error(field) === null ? null : `request-dialog-${field}-error`;
  }

  submit(): void {
    const kind = this.kind();
    if (kind === null || this.submitting()) {
      return;
    }
    const errors: Record<string, string> = {};
    let payload: DialogSubmit | null = null;
    switch (kind) {
      case 'assign':
        payload = {
          kind,
          assigneeUserId: this.assignee() === UNASSIGNED ? null : this.assignee(),
        };
        break;
      case 'change-priority':
        payload = { kind, urgency: this.urgency() };
        break;
      case 'set-branch':
        if (this.branchId() === '') {
          errors['branchId'] = 'Choose a branch.';
        }
        payload = { kind, branchId: this.branchId() };
        break;
      case 'request-information':
      case 'log-response': {
        const body = this.body().trim();
        if (body.length === 0) {
          errors['body'] = REQUIRED;
        } else if (body.length > 2000) {
          errors['body'] = 'Use 2000 characters or fewer.';
        }
        payload = { kind, body };
        break;
      }
      case 'cancel-request': {
        const reason = this.body().trim();
        if (reason.length === 0) {
          errors['reason'] = 'Enter a reason.';
        } else if (reason.length > 500) {
          errors['reason'] = 'Use 500 characters or fewer.';
        }
        payload = { kind, reason };
        break;
      }
      case 'schedule-assessment':
      case 'reschedule': {
        const start = this.start();
        const endTime = this.endTime();
        if (start === '') {
          errors['start'] = REQUIRED;
        }
        if (endTime === '') {
          errors['end'] = REQUIRED;
        } else if (start !== '' && endTime <= start.slice(11)) {
          errors['end'] = 'End time must be after the start time.';
        }
        if (this.needsBranch() && this.branchId() === '') {
          errors['branchId'] = 'Choose a branch.';
        }
        payload = {
          kind,
          body: {
            start,
            end: `${start.slice(0, 10)}T${endTime}`,
            technicianId: this.technicianId() === NO_TECHNICIAN ? null : this.technicianId(),
            ...(this.needsBranch() ? { branchId: this.branchId() } : {}),
          },
        };
        break;
      }
    }
    this.localErrors.set(errors);
    if (Object.keys(errors).length === 0 && payload !== null) {
      this.submitted.emit(payload);
    }
  }
}
