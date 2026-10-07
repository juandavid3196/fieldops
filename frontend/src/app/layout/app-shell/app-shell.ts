import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { FocusTrap } from 'primeng/focustrap';
import { IconField } from 'primeng/iconfield';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { ADMIN_QUERY, MD_QUERY, watchMedia } from '../../core/config/breakpoints';
import { comingSoonPath } from '../../core/config/coming-soon-modules';
import { SessionService } from '../../core/services/session.service';
import { ShellSidebar } from '../shell-sidebar/shell-sidebar';
import { ADMINISTRATION_ROLE_CODES, NAV_GROUPS } from './app-shell.nav';

export const SIGN_OUT_ERROR_MESSAGE = "We couldn't sign you out. Try again.";
export const SEARCH_PLACEHOLDER = 'Search requests, customers, work orders…';

/** FR-02: below 768px a drawer, 768-1099px the rail, from 1100px the full sidebar. */
export type LayoutMode = 'drawer' | 'rail' | 'full';
type MenuKind = 'org' | 'user';

/** Authenticated layout: navy sidebar (rail or drawer), top bar and the single `main`. */
@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterOutlet,
    Drawer,
    FocusTrap,
    IconField,
    InputIcon,
    InputText,
    Message,
    ShellSidebar,
    SpinnerIcon,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class AppShell {
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly navButton = viewChild<ElementRef<HTMLButtonElement>>('navButton');
  private readonly userButton = viewChild.required<ElementRef<HTMLButtonElement>>('userButton');
  private readonly orgButton = viewChild<ElementRef<HTMLButtonElement>>('orgButton');

  readonly session = this.sessionService.session;
  readonly signOutErrorMessage = SIGN_OUT_ERROR_MESSAGE;
  readonly searchPlaceholder = SEARCH_PLACEHOLDER;
  readonly helpLink = comingSoonPath('help');
  readonly notificationsLink = comingSoonPath('notifications');
  readonly drawerPt = {
    root: { role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'shell-drawer-title' },
  };

  /**
   * Navy drawer through PrimeNG's own `--p-drawer-*` design-token variables (no `.p-*` overrides).
   * `[dt]` is not used: it is also an input of the `pFocusTrap` directive on the same element.
   */
  readonly drawerStyle = {
    'inline-size': 'min(16rem, 88vw)',
    '--p-drawer-background': 'var(--fo-color-navy)',
    '--p-drawer-color': 'var(--fo-color-navy-text)',
    '--p-drawer-border-color': 'transparent',
    '--p-drawer-header-padding': '0',
    '--p-drawer-content-padding': '0',
  };

  readonly layoutMode = signal<LayoutMode>('full');
  /** Per page visit only (never persisted); `null` follows the layout mode default. Reset on mode change. */
  readonly collapsed = signal<boolean | null>(null);
  readonly drawerOpen = signal(false);
  readonly openMenu = signal<MenuKind | null>(null);
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);

  readonly isDrawer = computed(() => this.layoutMode() === 'drawer');
  readonly railActive = computed(
    () => !this.isDrawer() && (this.collapsed() ?? this.layoutMode() === 'rail'),
  );
  readonly menuOpen = computed(() => this.openMenu() === 'user');
  readonly orgMenuOpen = computed(() => this.openMenu() === 'org');

  readonly groups = computed(() =>
    NAV_GROUPS.filter((group) => {
      const role = this.session()?.role.code ?? '';
      return (
        (!group.adminOnly || ADMINISTRATION_ROLE_CODES.includes(role)) &&
        (!group.technicianOnly || role === 'technician')
      );
    }),
  );
  readonly showAdministration = computed(() =>
    this.groups().some((group) => group.adminOnly === true),
  );
  readonly fullName = computed(() => {
    const user = this.session()?.user;
    return [user?.firstName, user?.lastName]
      .map((part) => part?.trim() ?? '')
      .filter((part) => part !== '')
      .join(' ');
  });
  readonly initials = computed(() =>
    this.fullName()
      .split(' ')
      .filter((part) => part !== '')
      .map((part) => part.charAt(0).toUpperCase())
      .join(''),
  );
  readonly accountLabel = computed(
    () => `Account menu, ${this.fullName()}, ${this.session()?.role.name ?? ''}`,
  );
  readonly organizationName = computed(() => this.session()?.organization.name ?? '');

  constructor() {
    let mdMatches = true;
    let adminMatches = true;
    const apply = (): void => {
      const mode: LayoutMode = !mdMatches ? 'drawer' : adminMatches ? 'full' : 'rail';
      if (mode !== this.layoutMode()) {
        this.layoutMode.set(mode);
        this.collapsed.set(null);
        if (mode !== 'drawer') {
          this.drawerOpen.set(false);
        }
      }
    };
    const md = watchMedia(MD_QUERY, (matches) => {
      mdMatches = matches;
      apply();
    });
    const admin = watchMedia(ADMIN_QUERY, (matches) => {
      adminMatches = matches;
      apply();
    });
    mdMatches = md.matches;
    adminMatches = admin.matches;
    apply();
    this.destroyRef.onDestroy(() => {
      md.stop();
      admin.stop();
    });
  }

  openDrawer(): void {
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  /** FR-06: focus goes back to the button that opened the drawer. */
  onDrawerHide(): void {
    this.navButton()?.nativeElement.focus();
  }

  toggleCollapsed(): void {
    this.collapsed.set(!this.railActive());
  }

  /** Enter in the search field opens the Coming soon page; there are no suggestions. */
  onSearch(event: Event): void {
    event.preventDefault();
    void this.router.navigateByUrl(comingSoonPath('search'));
  }

  toggleMenu(kind: MenuKind = 'user'): void {
    if (this.openMenu() === kind) {
      this.closeMenu(true);
      return;
    }
    this.openMenu.set(kind);
    this.focusMenuItem();
  }

  /** The organization menu has one current entry: selecting it changes nothing and sends nothing. */
  selectOrganization(): void {
    this.closeMenu(true);
  }

  onMenuKeydown(event: KeyboardEvent): void {
    if (this.openMenu() === null) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeMenu(true);
    } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      this.focusMenuItem();
    }
  }

  onButtonKeydown(event: KeyboardEvent, kind: MenuKind): void {
    if (event.key === 'ArrowDown' && this.openMenu() !== kind) {
      event.preventDefault();
      this.toggleMenu(kind);
    } else {
      this.onMenuKeydown(event);
    }
  }

  onMenuFocusOut(event: FocusEvent, container: HTMLElement): void {
    const next = event.relatedTarget;
    if (this.openMenu() !== null && next instanceof Node && !container.contains(next)) {
      this.closeMenu(false);
    }
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (
      this.openMenu() !== null &&
      target instanceof Element &&
      target.closest('.shell__menu-wrap') === null
    ) {
      this.closeMenu(false);
    }
  }

  signOut(): void {
    if (this.signingOut()) {
      return;
    }

    this.signingOut.set(true);
    this.signOutFailed.set(false);
    this.sessionService
      .signOut()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.handleSignedOut(),
        error: () => this.handleSignOutFailed(),
      });
  }

  private handleSignedOut(): void {
    this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true }).then(
      (navigated) => {
        if (!navigated) {
          this.signingOut.set(false);
        }
      },
      () => this.signingOut.set(false),
    );
  }

  private handleSignOutFailed(): void {
    this.signingOut.set(false);
    this.signOutFailed.set(true);
    this.closeMenu(true);
  }

  private closeMenu(returnFocus: boolean): void {
    const kind = this.openMenu();
    this.openMenu.set(null);
    if (returnFocus && kind !== null) {
      afterNextRender(
        () => (kind === 'org' ? this.orgButton() : this.userButton())?.nativeElement.focus(),
        { injector: this.injector },
      );
    }
  }

  private focusMenuItem(): void {
    afterNextRender(
      () => this.host.querySelector<HTMLElement>('.shell__menu [role="menuitem"]')?.focus(),
      { injector: this.injector },
    );
  }
}
