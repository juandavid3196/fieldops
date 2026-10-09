import { Component, computed, inject, input, output, signal } from '@angular/core';
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

  /** Manual expand/collapse of groups by item link; unset groups follow the current URL. */
  private readonly toggled = signal<Readonly<Record<string, boolean>>>({});
  private readonly allItems = computed(() =>
    this.groups().flatMap((group) =>
      group.items.flatMap((item) => [item, ...(item.children ?? [])]),
    ),
  );

  /** A group with children is open when toggled so, else while the URL is under its prefix. */
  isOpen(item: NavItem): boolean {
    return this.toggled()[item.link] ?? this.isActive(item);
  }

  toggle(item: NavItem): void {
    const open = !this.isOpen(item);
    this.toggled.update((state) => ({ ...state, [item.link]: open }));
  }

  groupId(item: NavItem): string {
    return `sidebar-group-${item.label.toLowerCase().replace(/\W+/g, '-')}`;
  }

  /**
   * Exact items match only their own path; others match their `activePrefix` (default: link) and
   * children, unless another item owns the exact path (Products and services under `/admin`).
   */
  isActive(item: NavItem): boolean {
    const path = this.url().split(/[?#]/)[0];
    if (item.queryParams !== undefined) {
      return (
        path === item.link && this.queryMatches(item, this.router.parseUrl(this.url()).queryParams)
      );
    }
    if (item.exact) {
      return path === item.link;
    }
    if (this.allItems().some((other) => other.exact && other.link === path)) {
      return false;
    }
    const prefix = item.activePrefix ?? item.link;
    return path === prefix || path.startsWith(`${prefix}/`);
  }

  /**
   * The recognised query pairs of the URL (keys and values some item of the same link declares)
   * equal the item's own: an unknown value falls back to the default item (`queryParams: {}`).
   */
  private queryMatches(item: NavItem, query: Readonly<Record<string, string>>): boolean {
    const siblings = this.allItems().filter(
      (other) => other.link === item.link && other.queryParams !== undefined,
    );
    const pairs = (source: Readonly<Record<string, string>>): string[] =>
      Object.entries(source)
        .map(([key, value]) => `${key}=${value}`)
        .sort();
    const known = new Set(siblings.flatMap((other) => pairs(other.queryParams ?? {})));
    const present = pairs(query).filter((pair) => known.has(pair));
    return present.join('&') === pairs(item.queryParams ?? {}).join('&');
  }
}
