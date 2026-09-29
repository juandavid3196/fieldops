import { Component, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TooltipModule } from 'primeng/tooltip';
import { filter, map, startWith } from 'rxjs';

import { NavGroup, NavItem } from '../app-shell/app-shell.nav';

/**
 * Navy navigation (FR-01, FR-02). Full: brand, grouped items and Collapse. Rail: icons only,
 * each item keeping its accessible name and tooltip, group headings hidden. In the mobile drawer
 * (`drawer`) only the navigation list renders; the drawer supplies its own header.
 */
@Component({
  selector: 'app-shell-sidebar',
  imports: [RouterLink, TooltipModule],
  templateUrl: './shell-sidebar.html',
  styleUrl: './shell-sidebar.scss',
})
export class ShellSidebar {
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { requireSync: true },
  );

  readonly groups = input.required<readonly NavGroup[]>();
  readonly rail = input(false);
  readonly drawer = input(false);
  readonly navigated = output<void>();
  readonly closeRequested = output<void>();
  readonly collapseToggled = output<void>();

  /** Exact items match only their own path; others match their `activePrefix` (default: link) and children. */
  isActive(item: NavItem): boolean {
    const path = this.url().split(/[?#]/)[0];
    if (item.exact) {
      return path === item.link;
    }
    const prefix = item.activePrefix ?? item.link;
    return path === prefix || path.startsWith(`${prefix}/`);
  }
}
