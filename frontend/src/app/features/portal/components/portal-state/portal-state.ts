import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { ITEM_UNAVAILABLE_MESSAGE } from '../../models/portal.model';
import { ResourceState } from '../../utils/portal-resource';

/**
 * Loading, empty, error and not-found states of a portal list or detail. The ready content is
 * projected; `[empty]` content adds an action under the empty text. Announced politely.
 */
@Component({
  selector: 'app-portal-state',
  imports: [ButtonDirective, Message, RouterLink, Skeleton],
  template: `
    @switch (state()) {
      @case ('loading') {
        <div class="state" role="status" aria-live="polite" aria-busy="true">
          <span class="sr">{{ label() }}</span>
          <p-skeleton height="2rem" />
          <p-skeleton height="2rem" />
          <p-skeleton height="2rem" />
        </div>
      }
      @case ('empty') {
        <div class="state" role="status" aria-live="polite">
          <p class="state__text">{{ emptyText() }}</p>
          <ng-content select="[empty]" />
        </div>
      }
      @case ('error') {
        <div class="state" role="alert">
          <p-message severity="error">{{ errorText() }}</p-message>
          <button pButton type="button" severity="secondary" (click)="retry.emit()">
            Try again
          </button>
        </div>
      }
      @case ('not-found') {
        <div class="state" role="alert">
          <h2 class="state__title">{{ unavailable }}</h2>
          @if (backLink(); as link) {
            <a pButton severity="secondary" [routerLink]="link">{{ backLabel() }}</a>
          }
        </div>
      }
      @default {
        <ng-content />
      }
    }
  `,
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }

    .state {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      align-items: flex-start;
      padding: 1rem;
      background: var(--fo-color-surface);
      border: 1px solid var(--fo-color-border);
      border-radius: var(--fo-radius-card);
    }

    .state p-skeleton {
      inline-size: 100%;
    }

    .state__title {
      margin: 0;
      font-size: 1.25rem;
      color: var(--fo-color-heading);
    }

    .state__text {
      margin: 0;
      color: var(--fo-color-text-secondary);
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
export class PortalState {
  readonly state = input.required<ResourceState>();
  /** Accessible loading label, e.g. "Loading requests". */
  readonly label = input('Loading');
  readonly emptyText = input('Nothing here yet.');
  readonly errorText = input("We couldn't load this. Try again.");
  /** Route of the "Back to <module>" link of the not-found state. */
  readonly backLink = input<string | null>(null);
  readonly backLabel = input('Back');

  readonly retry = output<void>();

  protected readonly unavailable = ITEM_UNAVAILABLE_MESSAGE;
}
