import { Component, input, output, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { TranslocoPipe } from '@jsverse/transloco';

import { ITEM_UNAVAILABLE_MESSAGE } from '../../models/portal.model';
import { ResourceState } from '../../utils/portal-resource';

import { t } from '../../../../core/i18n/translate';
/**
 * Loading, empty, error and not-found states of a portal list or detail. The ready content is
 * projected; `[empty]` content adds an action under the empty text. Announced politely.
 */
@Component({
  selector: 'app-portal-state',
  imports: [TranslocoPipe, ButtonDirective, Message, RouterLink, Skeleton],
  template: `
    @switch (state()) {
      @case ('loading') {
        <div class="state" role="status" aria-live="polite" aria-busy="true">
          <span class="sr">{{ labelValue() }}</span>
          <p-skeleton height="2rem" />
          <p-skeleton height="2rem" />
          <p-skeleton height="2rem" />
        </div>
      }
      @case ('empty') {
        <div class="state state--empty" role="status" aria-live="polite">
          <span class="state__icon" aria-hidden="true"
            ><i class="pi" [class]="emptyIcon()"></i
          ></span>
          <p class="state__text">{{ emptyTextValue() }}</p>
          <ng-content select="[empty]" />
        </div>
      }
      @case ('error') {
        <div class="state" role="alert">
          <p-message severity="error">{{ errorTextValue() }}</p-message>
          <button pButton type="button" severity="secondary" (click)="retry.emit()">
            {{ 'common.tryAgain' | transloco }}
          </button>
        </div>
      }
      @case ('not-found') {
        <div class="state" role="alert">
          <h2 class="state__title">{{ unavailable }}</h2>
          @if (backLink(); as link) {
            <a pButton severity="secondary" [routerLink]="link">{{ backLabelValue() }}</a>
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

    .state--empty {
      align-items: center;
      padding: 2.5rem 1rem;
      text-align: center;
    }

    .state__icon {
      display: grid;
      place-items: center;
      inline-size: 3.5rem;
      block-size: 3.5rem;
      font-size: 1.5rem;
      color: var(--fo-color-action-blue);
      background: var(--fo-color-action-blue-soft);
      border-radius: 50%;
    }

    .state__icon .pi {
      font-size: inherit;
    }

    .state--empty .state__text {
      font-size: 1rem;
      color: var(--fo-color-heading);
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
  readonly label = input<string | null>(null);
  readonly labelValue = computed(() => this.label() ?? t('portal.portalState.loading'));
  readonly emptyText = input<string | null>(null);
  readonly emptyTextValue = computed(
    () => this.emptyText() ?? t('portal.portalState.nothingHereYet'),
  );
  /** PrimeIcons class of the empty-state tile. */
  readonly emptyIcon = input('pi-inbox');
  readonly errorText = input<string | null>(null);
  readonly errorTextValue = computed(
    () => this.errorText() ?? t('portal.portalState.weCouldntLoadThis'),
  );
  /** Route of the "Back to <module>" link of the not-found state. */
  readonly backLink = input<string | null>(null);
  readonly backLabel = input<string | null>(null);
  readonly backLabelValue = computed(() => this.backLabel() ?? t('common.back'));

  readonly retry = output<void>();

  protected get unavailable(): string {
    return ITEM_UNAVAILABLE_MESSAGE();
  }
}
