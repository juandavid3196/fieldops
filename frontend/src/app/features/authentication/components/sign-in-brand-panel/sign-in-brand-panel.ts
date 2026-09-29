import { NgOptimizedImage } from '@angular/common';
import { Component } from '@angular/core';

interface BrandItem {
  readonly title: string;
  readonly text: string;
  readonly icon: string;
}

interface StatusCard extends BrandItem {
  readonly tone: 'green' | 'blue' | 'purple';
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
  readonly statusCards: readonly StatusCard[] = [
    {
      title: 'Request approved',
      text: 'Customer request in, ready to schedule.',
      icon: 'pi-check',
      tone: 'green',
    },
    {
      title: 'Technician assigned',
      text: 'The right person for the job.',
      icon: 'pi-user',
      tone: 'blue',
    },
    {
      title: 'Invoice paid',
      text: 'Work complete. Payment received.',
      icon: 'pi-file',
      tone: 'purple',
    },
  ];

  readonly benefits: readonly BrandItem[] = [
    {
      title: 'Work runs smoother',
      text: 'From requests to payments, all in one place.',
      icon: 'pi-calendar',
    },
    {
      title: 'Happier customers',
      text: 'Faster response times and clear communication.',
      icon: 'pi-users',
    },
    {
      title: 'A more profitable business',
      text: 'Less admin work. More time for what matters.',
      icon: 'pi-chart-bar',
    },
  ];
}
