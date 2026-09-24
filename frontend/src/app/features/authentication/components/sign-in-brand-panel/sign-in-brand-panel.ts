import { NgOptimizedImage } from '@angular/common';
import { Component } from '@angular/core';

interface BrandItem {
  readonly title: string;
  readonly text: string;
}

/**
 * Decorative brand panel of the Sign In page (design 26); shown from `lg`.
 *
 * The photo stays lazy (no `priority`) so it is never downloaded below `lg`, where the
 * panel is hidden. From `lg` it is the LCP element, so dev builds log NG02955; Angular
 * has no supported per-image opt-out for that dev-only check, and `priority` would
 * preload the photo on mobile too. Accepted trade-off (sign-in spec, audit F-05).
 */
@Component({
  selector: 'app-sign-in-brand-panel',
  imports: [NgOptimizedImage],
  templateUrl: './sign-in-brand-panel.html',
  styleUrl: './sign-in-brand-panel.scss',
})
export class SignInBrandPanel {
  readonly statusCards: readonly BrandItem[] = [
    { title: 'Request approved', text: 'Customer request in, ready to schedule.' },
    { title: 'Technician assigned', text: 'The right person for the job.' },
    { title: 'Invoice paid', text: 'Work complete. Payment received.' },
  ];

  readonly benefits: readonly BrandItem[] = [
    { title: 'Work runs smoother', text: 'From requests to payments, all in one place.' },
    { title: 'Happier customers', text: 'Faster response times and clear communication.' },
    { title: 'A more profitable business', text: 'Less admin work. More time for what matters.' },
  ];
}
