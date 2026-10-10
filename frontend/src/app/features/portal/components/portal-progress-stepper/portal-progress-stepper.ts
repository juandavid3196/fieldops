import { Component, input } from '@angular/core';

import { VISIT_STEPS } from '../../models/portal.model';

/** Visit progress (BR-38): past steps complete, the current step marked, text and icon not color. */
@Component({
  selector: 'app-portal-progress-stepper',
  template: `
    <ol class="steps" aria-label="Appointment progress">
      @for (label of steps; track label; let index = $index) {
        <li
          class="steps__item"
          [class.steps__item--done]="index < step()"
          [class.steps__item--current]="index === step()"
          [attr.aria-current]="index === step() ? 'step' : null"
        >
          <span class="steps__marker" aria-hidden="true">
            @if (index < step()) {
              <i class="pi pi-check"></i>
            } @else if (index === step()) {
              <i class="pi pi-circle-fill"></i>
            }
          </span>
          <span class="steps__label">{{ label }}</span>
          <span class="sr">
            {{
              index < step() ? ', completed' : index === step() ? ', current step' : ', upcoming'
            }}
          </span>
        </li>
      }
    </ol>
  `,
  styles: `
    :host {
      display: block;
    }

    .steps {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .steps__item {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: 0.375rem;
      align-items: center;
      font-size: 0.8125rem;
      color: var(--fo-color-text-secondary);
      text-align: center;
    }

    .steps__item:not(:first-child)::before {
      position: absolute;
      inset-block-start: 0.6875rem;
      inset-inline-end: 50%;
      inline-size: 100%;
      block-size: 2px;
      content: '';
      background: var(--fo-color-border-strong);
    }

    .steps__item--done::before,
    .steps__item--current::before {
      background: var(--fo-color-primary);
    }

    .steps__marker {
      position: relative;
      z-index: 1;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: 1.5rem;
      block-size: 1.5rem;
      font-size: 0.625rem;
      color: var(--fo-color-on-primary);
      background: var(--fo-color-surface);
      border: 2px solid var(--fo-color-border-strong);
      border-radius: 50%;
    }

    .steps__item--done .steps__marker,
    .steps__item--current .steps__marker {
      background: var(--fo-color-primary);
      border-color: var(--fo-color-primary);
    }

    .steps__item--current {
      font-weight: 700;
      color: var(--fo-color-heading);
    }

    .sr {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class PortalProgressStepper {
  /** Zero-based current step (the appointment `progressStep`). */
  readonly step = input.required<number>();
  protected readonly steps = VISIT_STEPS;
}
