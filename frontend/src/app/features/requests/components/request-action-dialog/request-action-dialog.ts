import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { TranslocoPipe } from '@jsverse/transloco';

import { FormField } from '../../../organizations/components/form-field/form-field';
import {
  RequestActionId,
  RequestDetail,
  RequestOptions,
  URGENCY_LABELS,
  Urgency,
} from '../../models/requests.model';

import { localized, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
export type DialogKind = Extract<
  RequestActionId,
  | 'assign'
  | 'change-priority'
  | 'set-branch'
  | 'request-information'
  | 'log-response'
  | 'cancel-request'
>;

export type DialogSubmit =
  | { readonly kind: 'assign'; readonly assigneeUserId: string | null }
  | { readonly kind: 'change-priority'; readonly urgency: Urgency }
  | { readonly kind: 'set-branch'; readonly branchId: string }
  | { readonly kind: 'request-information' | 'log-response'; readonly body: string }
  | { readonly kind: 'cancel-request'; readonly reason: string };

export const DIALOG_KINDS: readonly RequestActionId[] = [
  'assign',
  'change-priority',
  'set-branch',
  'request-information',
  'log-response',
  'cancel-request',
];

export function isDialogKind(id: RequestActionId): id is DialogKind {
  return DIALOG_KINDS.includes(id);
}

interface Option {
  readonly code: string;
  readonly label: string;
}

const TITLES: Readonly<Record<DialogKind, string>> = localized(() => ({
  assign: t('requests.requestActionDialog.assignRequest'),
  'change-priority': t('requests.requestActionDialog.changePriority'),
  'set-branch': t('requests.requestActionDialog.setBranch'),
  'request-information': t('requests.requestActionDialog.requestInformation'),
  'log-response': t('requests.requestActionDialog.logCustomerResponse'),
  'cancel-request': t('requests.requestActionDialog.cancelRequest'),
}));

const SUBMIT_LABELS: Readonly<Record<DialogKind, string>> = localized(() => ({
  assign: t('requests.requestActionDialog.assign'),
  'change-priority': t('requests.requestActionDialog.savePriority'),
  'set-branch': t('requests.requestActionDialog.setBranch'),
  'request-information': t('requests.requestActionDialog.sendRequest'),
  'log-response': t('requests.requestActionDialog.logResponse'),
  'cancel-request': t('requests.requestActionDialog.cancelRequest'),
}));

const UNASSIGNED = 'unassigned';
const REQUIRED = (): string => t('requests.requestActionDialog.required');

/**
 * Action dialogs of the request panel (BR-09 to BR-15): one form per kind with client checks and
 * the page's `400` field errors inline. The page runs the mutation and owns 409 and failure
 * handling, so this component only collects and validates input.
 */
@Component({
  selector: 'app-request-action-dialog',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    Dialog,
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
  readonly submitting = input(false);
  /** `400` field errors keyed by camelCase field (`assigneeUserId`, `branchId`, `body`, ...). */
  readonly fieldErrors = input<Readonly<Record<string, string>>>({});

  readonly dismissed = output<void>();
  readonly submitted = output<DialogSubmit>();

  readonly assignee = signal(UNASSIGNED);
  readonly urgency = signal<Urgency>('standard');
  readonly branchId = signal('');
  readonly body = signal('');
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
      ? t('requests.requestActionDialog.cancel', { value: this.detail()?.number ?? 'request' })
      : TITLES[kind];
  });
  readonly submitLabel = computed(() => {
    const kind = this.kind();
    return kind === null ? '' : SUBMIT_LABELS[kind];
  });
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
      { code: UNASSIGNED, label: t('requests.requestActionDialog.unassigned') },
      ...eligible.map((member) => ({ code: member.userId, label: member.name })),
    ];
  });
  readonly branchOptions = computed<Option[]>(() =>
    (this.options()?.branches ?? []).map((branch) => ({ code: branch.id, label: branch.name })),
  );
  readonly messageLabel = computed(() =>
    this.kind() === 'request-information'
      ? t('requests.requestActionDialog.messageToTheCustomer')
      : t('requests.requestActionDialog.customerResponse'),
  );

  constructor() {
    effect(() => {
      const kind = this.kind();
      if (kind !== null) {
        untracked(() => this.reset());
      }
    });
  }

  private reset(): void {
    const detail = this.detail();
    this.localErrors.set({});
    this.body.set('');
    this.assignee.set(detail?.assignee?.userId ?? UNASSIGNED);
    this.urgency.set(detail?.urgency ?? 'standard');
    this.branchId.set(detail?.branch?.id ?? '');
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
          errors['branchId'] = t('requests.requestActionDialog.chooseABranch');
        }
        payload = { kind, branchId: this.branchId() };
        break;
      case 'request-information':
      case 'log-response': {
        const body = this.body().trim();
        if (body.length === 0) {
          errors['body'] = REQUIRED();
        } else if (body.length > 2000) {
          errors['body'] = t('requests.requestActionDialog.use2000CharactersOr');
        }
        payload = { kind, body };
        break;
      }
      case 'cancel-request': {
        const reason = this.body().trim();
        if (reason.length === 0) {
          errors['reason'] = t('requests.requestActionDialog.enterAReason');
        } else if (reason.length > 500) {
          errors['reason'] = t('requests.requestActionDialog.use500CharactersOr');
        }
        payload = { kind, reason };
        break;
      }
    }
    this.localErrors.set(errors);
    if (Object.keys(errors).length === 0 && payload !== null) {
      this.submitted.emit(payload);
    }
  }
}
