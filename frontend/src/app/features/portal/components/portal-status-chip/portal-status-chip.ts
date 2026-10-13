import { Component, computed, input } from '@angular/core';

import { ChipTone } from '../../utils/portal-status';

const ICONS: Readonly<Record<ChipTone, string>> = {
  info: 'pi-info-circle',
  warning: 'pi-exclamation-triangle',
  success: 'pi-check-circle',
  danger: 'pi-times-circle',
  neutral: 'pi-minus-circle',
};

/**
 * Status chip: the state is conveyed by its text label, never by color alone. `pill` adds an
 * icon; Design 28 uses `tag` (no marker) for request rows and `dot` for activity rows.
 */
@Component({
  selector: 'app-portal-status-chip',
  template: `
    <span class="chip" [attr.data-tone]="tone()" [class.chip--tag]="variant() === 'tag'">
      @if (variant() === 'pill') {
        <i class="pi" [class]="icon()" aria-hidden="true"></i>
      } @else if (variant() === 'dot') {
        <span class="chip__dot" aria-hidden="true"></span>
      }
      {{ label() }}
    </span>
  `,
  styles: `
    :host {
      display: inline-block;
    }

    .chip {
      display: inline-flex;
      gap: 0.375rem;
      align-items: center;
      padding: 0.25rem 0.625rem;
      font-size: 0.8125rem;
      font-weight: 600;
      white-space: nowrap;
      color: var(--fo-color-neutral-text);
      background: var(--fo-color-neutral-background);
      border-radius: var(--fo-radius-pill);
    }

    .chip__dot {
      inline-size: 0.5rem;
      block-size: 0.5rem;
      background: currentcolor;
      border-radius: 50%;
    }

    .chip--tag {
      padding: 0.3125rem 0.625rem;
      font-weight: 500;
      border-radius: var(--fo-radius-control);
    }

    .chip[data-tone='info'] {
      color: var(--fo-color-action-blue);
      background: var(--fo-color-action-blue-soft);
    }

    .chip[data-tone='warning'] {
      color: var(--fo-color-warning-text);
      background: var(--fo-color-warning-background);
    }

    .chip[data-tone='success'] {
      color: var(--fo-color-success-text);
      background: var(--fo-color-success-background);
    }

    .chip[data-tone='danger'] {
      color: var(--fo-color-danger-text);
      background: var(--fo-color-danger-background);
    }
  `,
})
export class PortalStatusChip {
  readonly label = input.required<string>();
  readonly tone = input<ChipTone>('neutral');
  readonly variant = input<'pill' | 'tag' | 'dot'>('pill');
  protected readonly icon = computed(() => ICONS[this.tone()]);
}
