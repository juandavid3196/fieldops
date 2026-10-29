import { Component, effect, inject, untracked } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { MessageService } from 'primeng/api';
import { Toast } from 'primeng/toast';

import { t } from '../../../core/i18n/translate';
import { DemoModeService } from '../../../core/services/demo-mode.service';

/**
 * Discreet "Read-only demo" badge for a shell top bar, plus the toast that explains a write the API refused. Renders
 * nothing outside a demo organization.
 */
@Component({
  selector: 'app-demo-notice',
  imports: [Toast, TranslocoPipe],
  providers: [MessageService],
  template: `
    <p-toast />
    @if (demo.active()) {
      <span class="demo-notice"
        ><i class="pi pi-lock" aria-hidden="true"></i>{{ 'demoMode.badge' | transloco }}</span
      >
    }
  `,
  styles: `
    .demo-notice {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      font-size: 0.75rem;
      font-weight: 600;
      white-space: nowrap;
      color: var(--p-primary-700);
      background: var(--p-primary-50);
    }

    :host-context(.app-dark) .demo-notice {
      color: var(--p-primary-200);
      background: color-mix(in srgb, var(--p-primary-400) 18%, transparent);
    }
  `,
})
export class DemoNotice {
  protected readonly demo = inject(DemoModeService);
  private readonly messages = inject(MessageService);
  private shown = this.demo.refusals();

  constructor() {
    effect(() => {
      const refusals = this.demo.refusals();
      untracked(() => {
        if (refusals !== this.shown) {
          this.shown = refusals;
          this.messages.add({ severity: 'info', summary: t('demoMode.refused'), life: 5000 });
        }
      });
    });
  }
}
