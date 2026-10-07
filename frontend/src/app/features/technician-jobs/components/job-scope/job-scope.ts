import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import type { ButtonDirectiveOptions } from 'primeng/types/button';

/** BR-16 read-only "Original scope" dialog opened from the task checklist. */
@Component({
  selector: 'app-job-scope',
  imports: [ButtonDirective, Dialog],
  templateUrl: './job-scope.html',
  styleUrl: './job-scope.scss',
})
export class JobScope {
  readonly visible = input.required<boolean>();
  readonly lines = input.required<readonly string[]>();
  readonly closed = output<void>();

  readonly countText = computed(() => {
    const count = this.lines().length;
    return `${count} scope ${count === 1 ? 'item' : 'items'}`;
  });

  readonly dialogStyle = { width: '26rem', maxWidth: '94vw' };
  /** Circular outlined close button from the scope list design. */
  readonly closeProps: ButtonDirectiveOptions = {
    severity: 'secondary',
    variant: 'outlined',
    rounded: true,
    style: {
      width: '2.75rem',
      height: '2.75rem',
      color: 'var(--fo-color-heading)',
      borderColor: 'var(--fo-color-border-strong)',
    },
  };

  number(index: number): string {
    return String(index + 1).padStart(2, '0');
  }
}
