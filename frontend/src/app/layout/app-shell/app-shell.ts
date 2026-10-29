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
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { FocusTrap } from 'primeng/focustrap';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';
import { filter } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { ADMIN_QUERY, MD_QUERY, watchMedia } from '../../core/config/breakpoints';
import { comingSoonPath } from '../../core/config/coming-soon-modules';
import { canSeeDashboard } from '../../core/config/dashboard-roles';
import { MessageUnreadService } from '../../core/services/message-unread.service';
import { NotificationUnreadService } from '../../core/services/notification-unread.service';
import { SessionService } from '../../core/services/session.service';
import { NotificationPopover } from '../../features/notifications/components/notification-popover/notification-popover';
import { AppearanceMenuItem } from '../../shared/components/appearance-menu-item/appearance-menu-item';
import { AppearanceToast } from '../../shared/components/appearance-toast/appearance-toast';
import { LanguageMenuItem } from '../../shared/components/language-menu-item/language-menu-item';
import { DemoNotice } from '../../shared/components/demo-notice/demo-notice';
import { LanguageToast } from '../../shared/components/language-toast/language-toast';
import { UserAvatar } from '../../shared/components/user-avatar/user-avatar';
import { fullNameOf, initialsOf } from '../../shared/utils/initials';
import { ShellSidebar } from '../shell-sidebar/shell-sidebar';
import { GlobalSearch } from './global-search/global-search';
import { ADMINISTRATION_ROLE_CODES, MESSAGES_ROLE_CODES, NAV_GROUPS } from './app-shell.nav';

import { t } from '../../core/i18n/translate';
import { CodeLabelPipe } from '../../core/i18n/code-label.pipe';
export const SIGN_OUT_ERROR_MESSAGE = (): string => t('shell.appShell.signOutErrorMessage');

/** FR-02: below 768px a drawer, 768-1099px the rail, from 1100px the full sidebar. */
export type LayoutMode = 'drawer' | 'rail' | 'full';
type MenuKind = 'org' | 'user';

/** localStorage key of the sidebar choice ('1' rail, '0' full); absent follows the layout mode. */
const SIDEBAR_COLLAPSED_KEY = 'fieldops.sidebar.collapsed';

function readCollapsed(): boolean | null {
  try {
    const value = localStorage.getItem(SIDEBAR_COLLAPSED_KEY);
    return value === '1' ? true : value === '0' ? false : null;
  } catch {
    return null;
  }
}

function writeCollapsed(value: boolean): void {
  try {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, value ? '1' : '0');
  } catch {
    // Storage errors never break the layout.
  }
}

/** Authenticated layout: navy sidebar (rail or drawer), top bar and the single `main`. */
@Component({
  selector: 'app-shell',
  imports: [
    CodeLabelPipe,
    TranslocoPipe,
    RouterLink,
    RouterOutlet,
    Drawer,
    FocusTrap,
    GlobalSearch,
    AppearanceMenuItem,
    AppearanceToast,
    LanguageMenuItem,
    LanguageToast,
    DemoNotice,
    Message,
    NotificationPopover,
    ShellSidebar,
    SpinnerIcon,
    UserAvatar,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class AppShell {
  private readonly sessionService = inject(SessionService);
  private readonly unread = inject(NotificationUnreadService);
  private readonly messageUnread = inject(MessageUnreadService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly navButton = viewChild<ElementRef<HTMLButtonElement>>('navButton');
  private readonly userButton = viewChild.required<ElementRef<HTMLButtonElement>>('userButton');
  private readonly orgButton = viewChild<ElementRef<HTMLButtonElement>>('orgButton');

  readonly session = this.sessionService.session;
  get signOutErrorMessage(): string {
    return SIGN_OUT_ERROR_MESSAGE();
  }
  readonly helpLink = '/help';
  readonly comingSoonNotificationsLink = comingSoonPath('notifications');
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
  /** The user's last sidebar choice, kept across reloads; `null` follows the layout mode default. */
  readonly collapsed = signal<boolean | null>(readCollapsed());
  readonly drawerOpen = signal(false);
  private readonly searchOpen = signal(false);
  /** Drawer layout only: the search takes over the top bar until it is closed. */
  readonly mobileSearch = computed(() => this.isDrawer() && this.searchOpen());
  readonly openMenu = signal<MenuKind | null>(null);
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);

  readonly isDrawer = computed(() => this.layoutMode() === 'drawer');
  readonly railActive = computed(
    () => !this.isDrawer() && (this.collapsed() ?? this.layoutMode() === 'rail'),
  );
  readonly menuOpen = computed(() => this.openMenu() === 'user');
  readonly orgMenuOpen = computed(() => this.openMenu() === 'org');

  readonly groups = computed(() => {
    const role = this.session()?.role.code ?? '';
    const manage = canSeeDashboard(role);
    return (
      NAV_GROUPS.filter(
        (group) =>
          (!group.manageOnly || manage) &&
          (!group.hiddenForManage || !manage) &&
          (!group.adminOnly || ADMINISTRATION_ROLE_CODES.includes(role)) &&
          (!group.technicianOnly || role === 'technician') &&
          (!group.internalOnly || role !== 'technician'),
      )
        .map((group) => ({
          ...group,
          items: group.items.filter(
            (item) => item.roles === undefined || item.roles.includes(role),
          ),
        }))
        // A heading whose items are all hidden is not shown either.
        .filter((group) => group.items.length > 0)
    );
  });
  readonly showAdministration = computed(() =>
    this.groups().some((group) => group.adminOnly === true),
  );
  /** notifications-center BR-16/BR-17: the technician keeps the Coming soon bell, with no badge. */
  readonly internalRole = computed(() => this.session()?.role.code !== 'technician');
  readonly notificationsLink = computed(() =>
    this.internalRole() ? '/notifications' : this.comingSoonNotificationsLink,
  );
  readonly unreadCount = computed(() => (this.internalRole() ? (this.unread.count() ?? 0) : 0));
  readonly unreadBadge = computed(() =>
    this.unreadCount() > 99 ? '99+' : String(this.unreadCount()),
  );
  readonly notificationsLabel = computed(() =>
    this.unreadCount() > 0
      ? t('shell.appShell.notificationsUnread', { unreadCount: this.unreadCount() })
      : t('shell.appShell.notifications'),
  );
  /** technician-messages BR-15: sidebar badge of the Messages item (Owner, Operations, Dispatcher). */
  readonly messagesBadge = this.messageUnread.badge;
  readonly messagesLabel = this.messageUnread.label;
  readonly fullName = computed(() => fullNameOf(this.session()?.user));
  readonly initials = computed(() => initialsOf(this.fullName()));
  readonly photoUpdatedAt = computed(() => this.session()?.user.photoUpdatedAt ?? null);
  readonly accountLabel = computed(() =>
    t('shell.appShell.accountMenu', {
      fullName: this.fullName(),
      value: this.session()?.role.name ?? '',
    }),
  );
  readonly organizationName = computed(() => this.session()?.organization.name ?? '');

  constructor() {
    let mdMatches = true;
    let adminMatches = true;
    const apply = (): void => {
      const mode: LayoutMode = !mdMatches ? 'drawer' : adminMatches ? 'full' : 'rail';
      if (mode !== this.layoutMode()) {
        this.layoutMode.set(mode);
        this.collapsed.set(readCollapsed());
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

    // technician-messages BR-14: the Messages summary polls for the roles that see the item.
    this.messageUnread.reset();
    if (MESSAGES_ROLE_CODES.includes(this.session()?.role.code ?? '')) {
      this.messageUnread.startPolling();
    }
    this.destroyRef.onDestroy(() => this.messageUnread.reset());

    // BR-16: the count loads when the shell renders and on navigation (30 s reuse in the service),
    // then polls every 30 s while the tab is visible (internal roles only).
    if (this.internalRole()) {
      this.unread.reset();
      this.unread.refresh();
      this.unread.startPolling();
      this.router.events
        .pipe(
          filter((event) => event instanceof NavigationEnd),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe(() => {
          if (this.internalRole()) {
            this.unread.refresh();
          }
        });
    }

    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.searchOpen.set(false));
  }

  openSearch(): void {
    this.searchOpen.set(true);
  }

  closeSearch(): void {
    this.searchOpen.set(false);
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
    const next = !this.railActive();
    this.collapsed.set(next);
    writeCollapsed(next);
  }

  toggleMenu(kind: MenuKind = 'user'): void {
    if (this.openMenu() === kind) {
      this.closeMenu(true);
      return;
    }
    this.openMenu.set(kind);
    this.focusMenuItem();
  }

  /** Account links: the navigation itself moves focus, so the menu only closes. */
  onMenuLinkClick(): void {
    this.closeMenu(false);
  }

  /** BR-18: choosing a language closes the menu and returns focus to the menu button. */
  onLanguageChosen(): void {
    this.closeMenu(true);
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
      this.moveMenuFocus(event.key);
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
    this.unread.reset();
    this.messageUnread.reset();
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

  /** Roving focus over the open menu's items; wraps at both ends. */
  private moveMenuFocus(key: string): void {
    const items = Array.from(
      this.host.querySelectorAll<HTMLElement>('.shell__menu [role="menuitem"]'),
    );
    if (items.length === 0) {
      return;
    }
    const current = items.indexOf(document.activeElement as HTMLElement);
    const last = items.length - 1;
    const next =
      key === 'Home'
        ? 0
        : key === 'End'
          ? last
          : key === 'ArrowDown'
            ? (current + 1) % items.length
            : current <= 0
              ? last
              : current - 1;
    items[next].focus();
  }

  private focusMenuItem(): void {
    afterNextRender(
      () => this.host.querySelector<HTMLElement>('.shell__menu [role="menuitem"]')?.focus(),
      { injector: this.injector },
    );
  }
}
