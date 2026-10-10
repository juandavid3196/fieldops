import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Textarea } from 'primeng/textarea';

import { isApiError } from '../../../../core/models/api-error.model';
import { FormField } from '../../../organizations/components/form-field/form-field';
import { GENERIC_ERROR_MESSAGE, RATE_LIMITED_MESSAGE } from '../../models/portal.model';
import { PortalDashboardService } from '../../services/portal-dashboard.service';

export const MESSAGE_LIMIT = 1000;
export const MESSAGE_REQUIRED = 'Enter your message.';
export const MESSAGE_TOO_LONG = 'Message must be 1,000 characters or fewer.';

export function messageSentText(organizationName: string): string {
  return `Your message was sent. ${organizationName} will reply by email or phone.`;
}

/** Validation of the message (BR-35): 1 to 1,000 characters after trim. */
export function messageError(value: string): string | null {
  const length = value.trim().length;
  if (length === 0) {
    return MESSAGE_REQUIRED;
  }
  return length > MESSAGE_LIMIT ? MESSAGE_TOO_LONG : null;
}

/** "Send a message" dialog (BR-35): one submission; success replaces the form with a confirmation. */
@Component({
  selector: 'app-portal-message-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, FormField, SpinnerIcon, Textarea],
  templateUrl: './portal-message-dialog.html',
  styles: `
    :host {
      display: contents;
    }

    .dialog {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      min-inline-size: 0;
    }

    .dialog__text,
    .dialog__count {
      margin: 0;
      color: var(--fo-color-text-secondary);
      overflow-wrap: anywhere;
    }

    .dialog__count {
      font-size: 0.8125rem;
      text-align: end;
    }

    .dialog__failure {
      margin: 0;
      color: var(--fo-color-danger-text);
    }

    .dialog__sent {
      margin: 0;
      color: var(--fo-color-success-text);
      overflow-wrap: anywhere;
    }
  `,
})
export class PortalMessageDialog {
  private readonly dashboard = inject(PortalDashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private trigger: HTMLElement | null = null;

  readonly visible = input(false);
  readonly organizationName = input.required<string>();

  readonly dismissed = output<void>();
  /** `409 messaging_unavailable`: the organization cannot receive messages; the button hides. */
  readonly unavailable = output<void>();

  readonly message = signal('');
  readonly error = signal<string | null>(null);
  readonly failure = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly sent = signal(false);
  readonly sentText = computed(() => messageSentText(this.organizationName()));
  readonly limit = MESSAGE_LIMIT;

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => {
          const active = this.document.activeElement;
          this.trigger = active instanceof HTMLElement ? active : null;
          this.message.set('');
          this.error.set(null);
          this.failure.set(null);
          this.sent.set(false);
        });
      }
    });
  }

  close(): void {
    if (this.submitting()) {
      return;
    }
    this.dismissed.emit();
    const trigger = this.trigger;
    this.trigger = null;
    afterNextRender(() => trigger?.focus(), { injector: this.injector });
  }

  submit(): void {
    if (this.submitting() || this.sent()) {
      return;
    }
    const error = messageError(this.message());
    this.error.set(error);
    this.failure.set(null);
    if (error !== null) {
      afterNextRender(() => this.document.getElementById('portal-message-text')?.focus(), {
        injector: this.injector,
      });
      return;
    }

    this.submitting.set(true);
    this.dashboard
      .sendMessage(this.message().trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.sent.set(true);
        },
        error: (failure: unknown) => this.fail(failure),
      });
  }

  private fail(failure: unknown): void {
    this.submitting.set(false);
    const apiError = isApiError(failure) ? failure : null;
    if (apiError?.status === 409) {
      this.unavailable.emit();
    } else if (apiError?.kind === 'validation') {
      this.error.set(
        this.message().trim().length > MESSAGE_LIMIT ? MESSAGE_TOO_LONG : MESSAGE_REQUIRED,
      );
    } else if (apiError?.kind === 'rate-limited') {
      this.failure.set(RATE_LIMITED_MESSAGE);
    } else {
      this.failure.set(GENERIC_ERROR_MESSAGE);
    }
  }
}
