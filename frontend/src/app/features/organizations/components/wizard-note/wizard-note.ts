import { Component } from '@angular/core';

/** Informational note with an info icon; the text is projected. */
@Component({
  selector: 'app-wizard-note',
  template: `
    <p class="wizard-note">
      <i class="pi pi-info-circle" aria-hidden="true"></i>
      <span><ng-content /></span>
    </p>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .wizard-note {
      display: flex;
      gap: 0.5rem;
      margin: 0;
      padding: 0.75rem 1rem;
      border-radius: var(--fo-radius-panel);
      background: var(--fo-color-info-background);
      color: var(--fo-color-info-text);
      font-size: 0.875rem;
    }

    i {
      margin-block-start: 0.125rem;
    }
  `,
})
export class WizardNote {}
