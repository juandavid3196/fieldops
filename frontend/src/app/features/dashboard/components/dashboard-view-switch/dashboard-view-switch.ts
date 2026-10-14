import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export type DashboardView = 'business' | 'operations';

/**
 * business-overview BR-01: the Owner's labelled Business / Operations switch. Each option is a
 * link to `/dashboard` with only the new `view`, which drops the other view's parameters.
 */
@Component({
  selector: 'app-dashboard-view-switch',
  imports: [RouterLink],
  template: `
    <nav class="switch" aria-label="Dashboard view">
      @for (option of options; track option.id) {
        <a
          class="switch__option"
          routerLink="/dashboard"
          [queryParams]="{ view: option.id }"
          [attr.aria-current]="view() === option.id ? 'page' : null"
          >{{ option.name }}</a
        >
      }
    </nav>
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .switch {
      display: inline-flex;
      padding: 0.125rem;
      background: var(--fo-color-surface-muted);
      border: 1px solid var(--fo-color-border-strong);
      border-radius: var(--fo-radius-control);

      &__option {
        display: inline-flex;
        align-items: center;
        min-block-size: calc(var(--fo-size-control) - 0.5rem);
        padding: 0 0.875rem;
        font-weight: 600;
        color: var(--fo-color-text);
        text-decoration: none;
        border-radius: var(--fo-radius-control);

        &[aria-current='page'] {
          color: var(--fo-color-on-primary);
          background: var(--fo-color-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--fo-focus-ring);
        }

        @media (max-width: 47.99rem) {
          min-block-size: calc(var(--fo-size-touch-target) - 0.5rem);
        }
      }
    }
  `,
})
export class DashboardViewSwitch {
  readonly view = input.required<DashboardView>();

  readonly options: readonly { readonly id: DashboardView; readonly name: string }[] = [
    { id: 'business', name: 'Business' },
    { id: 'operations', name: 'Operations' },
  ];
}
