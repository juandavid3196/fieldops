import { NgTemplateOutlet } from '@angular/common';
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
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { FocusTrap } from 'primeng/focustrap';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';

import { SessionService } from '../../core/services/session.service';

export const SIGN_OUT_ERROR_MESSAGE = "We couldn't sign you out. Try again.";

/** BR-01: role codes allowed to see the Administration group. */
const COMPANY_SETTINGS_ROLE_CODES: readonly string[] = ['owner', 'viewer'];
/** `lg` from `styles/_breakpoints.scss`. */
const DESKTOP_QUERY = '(min-width: 64rem)';

/** Authenticated layout: top bar, navigation (sidebar or drawer) and the single `main`. */
@Component({
  selector: 'app-shell',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    Drawer,
    FocusTrap,
    Message,
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
  private readonly navButton = viewChild<ElementRef<HTMLButtonElement>>('navButton');
  private readonly userButton = viewChild.required<ElementRef<HTMLButtonElement>>('userButton');
  private readonly userMenu = viewChild<ElementRef<HTMLElement>>('userMenu');

  readonly session = this.sessionService.session;
  readonly signOutErrorMessage = SIGN_OUT_ERROR_MESSAGE;
  readonly drawerPt = {
    root: { role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'shell-drawer-title' },
  };

  readonly isDesktop = signal(true);
  readonly drawerOpen = signal(false);
  readonly menuOpen = signal(false);
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);

  readonly showAdministration = computed(() =>
    COMPANY_SETTINGS_ROLE_CODES.includes(this.session()?.role.code ?? ''),
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

  constructor() {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const query = window.matchMedia(DESKTOP_QUERY);
      this.isDesktop.set(query.matches);
      const listener = (event: MediaQueryListEvent): void => {
        this.isDesktop.set(event.matches);
        if (event.matches) {
          this.drawerOpen.set(false);
        }
      };
      query.addEventListener('change', listener);
      this.destroyRef.onDestroy(() => query.removeEventListener('change', listener));
    }
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

  toggleMenu(): void {
    if (this.menuOpen()) {
      this.closeMenu(true);
      return;
    }
    this.menuOpen.set(true);
    this.focusMenuItem();
  }

  onMenuKeydown(event: KeyboardEvent): void {
    if (!this.menuOpen()) {
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

  onUserKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' && !this.menuOpen()) {
      event.preventDefault();
      this.toggleMenu();
    } else {
      this.onMenuKeydown(event);
    }
  }

  onUserFocusOut(event: FocusEvent, container: HTMLElement): void {
    const next = event.relatedTarget;
    if (this.menuOpen() && next instanceof Node && !container.contains(next)) {
      this.closeMenu(false);
    }
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (
      this.menuOpen() &&
      target instanceof Node &&
      !this.userButton().nativeElement.parentElement?.contains(target)
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
    this.menuOpen.set(false);
    if (returnFocus) {
      afterNextRender(() => this.userButton().nativeElement.focus(), { injector: this.injector });
    }
  }

  private focusMenuItem(): void {
    afterNextRender(
      () => this.userMenu()?.nativeElement.querySelector<HTMLElement>('[role="menuitem"]')?.focus(),
      { injector: this.injector },
    );
  }
}
