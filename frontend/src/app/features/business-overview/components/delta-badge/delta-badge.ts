import { Component, computed, input } from '@angular/core';

import { Delta } from '../../utils/business-view';

const ICONS = { up: 'pi-arrow-up', down: 'pi-arrow-down', flat: 'pi-minus' } as const;

/** BR-11/BR-17: a change with an icon and text, never color alone. */
@Component({
  selector: 'app-delta-badge',
  template: `<span class="delta" [attr.data-tone]="tone()"
    ><i class="pi" [class]="icon()" aria-hidden="true"></i>{{ delta().text }}
    <span class="sr">{{ label() }}</span></span
  >`,
  styles: `
    :host {
      display: inline-flex;
    }

    .delta {
      display: inline-flex;
      gap: 0.25rem;
      align-items: center;
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--fo-color-neutral-text);
    }

    .delta[data-tone='good'] {
      color: var(--fo-color-success-text);
    }

    .delta[data-tone='bad'] {
      color: var(--fo-color-danger-text);
    }

    .delta i {
      font-size: 0.75rem;
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
export class DeltaBadge {
  readonly delta = input.required<Delta>();
  readonly label = input('vs previous period');
  /** Overtime going up is unfavourable. */
  readonly badWhenUp = input(false);
  readonly tone = computed(() => {
    const direction = this.delta().direction;
    return direction === 'flat'
      ? 'flat'
      : (direction === 'up') !== this.badWhenUp()
        ? 'good'
        : 'bad';
  });
  readonly icon = computed(() => ICONS[this.delta().direction]);
}
