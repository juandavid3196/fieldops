import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { Message } from 'primeng/message';

import { MD_QUERY, watchMedia } from '../../core/config/breakpoints';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalOrganizationService } from '../../features/portal/services/portal-organization.service';
import { PortalUpdatesStore } from '../../features/portal/services/portal-updates.store';
import { initialsOf } from '../../features/portal/utils/portal-format';
import {
  HELP_FRAGMENT,
  PORTAL_NAV,
  SIGN_IN_LINK,
  UPDATES_LINK,
  bellCountText,
} from './portal-shell.nav';

export const PORTAL_SIGN_OUT_ERROR_MESSAGE = "We couldn't sign you out. Try again.";
export const PORTAL_SWITCH_ERROR_MESSAGE = "We couldn't switch accounts. Try again.";

/**
 * Customer portal layout (BR-16): organization logo or initials and name, primary navigation, Help,
 * the bell with the unread count and the user menu. Below 768 px the navigation is a drawer.
 */
@Component({
  selector: 'app-portal-shell',
  imports: [Drawer, Message, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './portal-shell.html',
  styleUrl: './portal-shell.scss',
  host: {
    '(document:keydown.escape)': 'closeMenu()',
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class PortalShell {
  private readonly sessions = inject(PortalSessionService);
  private readonly updates = inject(PortalUpdatesStore);
  private readonly organization = inject(PortalOrganizationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');

  private logoObjectUrl: string | null = null;

  readonly nav = PORTAL_NAV;
  readonly updatesLink = UPDATES_LINK;
  readonly helpFragment = HELP_FRAGMENT;

  readonly session = this.sessions.session;
  readonly isDrawer = signal(false);
  readonly drawerOpen = signal(false);
  readonly menuOpen = signal(false);
  readonly busy = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly logoUrl = signal<string | null>(null);

  readonly fullName = computed(() => {
    const user = this.session()?.user;
    return [user?.firstName, user?.lastName]
      .map((part) => part?.trim() ?? '')
      .filter((part) => part !== '')
      .join(' ');
  });
  readonly initials = computed(() =>
    initialsOf(this.session()?.user.firstName ?? '', this.session()?.user.lastName ?? ''),
  );
  /** Account identity: the effects below run once per account, not per session revalidation. */
  private readonly contactId = computed(() => this.session()?.account.contactId);
  readonly organizationName = computed(() => this.session()?.account.organizationName ?? '');
  readonly organizationInitials = computed(() => initialsOf(this.organizationName()));
  readonly canSwitch = computed(() => (this.session()?.accounts.length ?? 0) > 1);
  readonly otherAccounts = computed(() => {
    const session = this.session();
    return session === null
      ? []
      : session.accounts.filter((account) => account.contactId !== session.account.contactId);
  });
  readonly bellText = computed(() => bellCountText(this.updates.unreadCount()));
  readonly bellLabel = computed(() => {
    const count = this.updates.unreadCount();
    return count > 0 ? `Updates, ${count} unread` : 'Updates';
  });

  constructor() {
    const md = watchMedia(MD_QUERY, (matches) => this.setDrawerMode(!matches));
    this.setDrawerMode(!md.matches);
    this.destroyRef.onDestroy(() => {
      md.stop();
      this.releaseLogo();
    });

    // Once per account: the unread count of the bell and the organization logo.
    effect(() => {
      if (this.contactId() === undefined) {
        return;
      }
      untracked(() => {
        this.updates
          .load()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({ error: () => this.updates.reset() });
        this.loadLogo(this.session()?.account.hasLogo ?? false);
      });
    });
  }

  openDrawer(): void {
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  onDrawerHide(): void {
    this.menuButton()?.nativeElement.focus();
  }

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (
      this.menuOpen() &&
      target instanceof Element &&
      target.closest('[data-portal-menu]') === null
    ) {
      this.closeMenu();
    }
  }

  switchAccount(contactId: string): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set(null);
    this.sessions
      .switchAccount(contactId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.closeMenu();
          this.updates.reset();
          // Home reloads with the selector reset (BR-07); other pages return to Home.
          void this.router.navigateByUrl('/portal');
        },
        error: () => {
          this.busy.set(false);
          this.errorMessage.set(PORTAL_SWITCH_ERROR_MESSAGE);
          this.closeMenu();
        },
      });
  }

  signOut(): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set(null);
    this.sessions
      .signOut()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.updates.reset();
          void this.router
            .navigateByUrl(SIGN_IN_LINK, { replaceUrl: true })
            .finally(() => this.busy.set(false));
        },
        error: () => {
          this.busy.set(false);
          this.errorMessage.set(PORTAL_SIGN_OUT_ERROR_MESSAGE);
          this.closeMenu();
        },
      });
  }

  private setDrawerMode(drawer: boolean): void {
    this.isDrawer.set(drawer);
    if (!drawer) {
      this.drawerOpen.set(false);
    }
  }

  private loadLogo(hasLogo: boolean): void {
    this.releaseLogo();
    if (!hasLogo) {
      return;
    }
    this.organization
      .logo()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.logoObjectUrl = URL.createObjectURL(blob);
          this.logoUrl.set(this.logoObjectUrl);
        },
        error: () => this.logoUrl.set(null),
      });
  }

  private releaseLogo(): void {
    if (this.logoObjectUrl !== null) {
      URL.revokeObjectURL(this.logoObjectUrl);
      this.logoObjectUrl = null;
    }
    this.logoUrl.set(null);
  }
}
