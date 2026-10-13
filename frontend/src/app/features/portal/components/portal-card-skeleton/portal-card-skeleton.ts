import { Component, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';

/** Per-card loading placeholder of Home; announced once by the page, hidden from assistive tech. */
@Component({
  selector: 'app-portal-card-skeleton',
  imports: [Skeleton],
  template: `
    <div class="card" aria-hidden="true">
      <p-skeleton width="45%" height="1.5rem" />
      <p-skeleton width="80%" [height]="height()" />
      <p-skeleton width="60%" height="1rem" />
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      padding: 1rem;
      background: var(--fo-color-surface);
      border: 1px solid var(--fo-color-border);
      border-radius: var(--fo-radius-card);
    }
  `,
})
export class PortalCardSkeleton {
  readonly height = input('4rem');
}
