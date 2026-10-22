import { Component, input } from '@angular/core';

/** BR-22: an info icon whose text is available to keyboard (focus) and screen readers. */
@Component({
  selector: 'app-info-hint',
  template: `<span class="hint" tabindex="0" role="img" [attr.aria-label]="text()" [title]="text()">
    @if (filled()) {
      <span class="hint__dot" aria-hidden="true"></span>
    } @else {
      <i class="pi pi-info-circle" aria-hidden="true"></i>
    }
  </span>`,
  styles: `
    :host {
      display: inline-flex;
    }

    .hint {
      display: inline-flex;
      align-items: center;
      font-size: 0.875rem;
      color: var(--fo-color-info-text);
      cursor: help;
      border-radius: var(--fo-radius-pill);

      &:focus-visible {
        outline: none;
        box-shadow: var(--fo-focus-ring);
      }
    }

    .hint__dot {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: 1.25rem;
      block-size: 1.25rem;
      font-size: 0.8125rem;
      font-weight: 800;
      line-height: 1;
      color: var(--fo-color-on-strong);
      background: var(--fo-color-action-blue);
      border-radius: var(--fo-radius-pill);

      &::after {
        content: 'i';
      }
    }
  `,
})
export class InfoHint {
  readonly text = input.required<string>();
  readonly filled = input(false);
}
