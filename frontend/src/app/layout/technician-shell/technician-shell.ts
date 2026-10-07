import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet,
} from '@angular/router';
import { Message } from 'primeng/message';
import { filter } from 'rxjs';

import { comingSoonModuleName } from '../../core/config/coming-soon-modules';
import { SessionService } from '../../core/services/session.service';
import { TechnicianVisitsService } from '../../features/technician-jobs/services/technician-visits.service';
import { avatarTextColor } from '../../features/technician-jobs/utils/technician-format';
import { NOTIFICATIONS_LINK, PROFILE_LINK, TECHNICIAN_NAV } from './technician-shell.nav';

export const SIGN_OUT_ERROR_MESSAGE = "We couldn't sign you out. Try again.";
type PanelKind = 'more' | 'account';

function deepest(snapshot: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let current = snapshot;
  while (current.firstChild !== null) {
    current = current.firstChild;
  }
  return current;
}

/** Technician layout: top bar, the single `main` and a fixed bottom navigation (BR-14, BR-15, BR-19). */
@Component({
  selector: 'app-technician-shell',
  imports: [RouterLink, RouterOutlet, Message],
  templateUrl: './technician-shell.html',
  styleUrl: './technician-shell.scss',
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'closePanel()',
  },
})
export class TechnicianShell {
  private readonly sessionService = inject(SessionService);
  private readonly visits = inject(TechnicianVisitsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly nav = TECHNICIAN_NAV;
  readonly notificationsLink = NOTIFICATIONS_LINK;
  readonly profileLink = PROFILE_LINK;
  readonly signOutErrorMessage = SIGN_OUT_ERROR_MESSAGE;

  readonly openPanel = signal<PanelKind | null>(null);
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);
  private readonly path = signal(this.currentPath());
  readonly pageTitle = signal(this.titleFor());

  readonly moreOpen = computed(() => this.openPanel() === 'more');
  readonly accountOpen = computed(() => this.openPanel() === 'account');
  readonly fullName = computed(() => {
    const user = this.sessionService.session()?.user;
    return [user?.firstName, user?.lastName]
      .map((part) => part?.trim() ?? '')
      .filter((part) => part !== '')
      .join(' ');
  });
  readonly initials = computed(
    () =>
      this.visits.technician()?.initials ??
      this.fullName()
        .split(' ')
        .filter((part) => part !== '')
        .map((part) => part.charAt(0).toUpperCase())
        .join(''),
  );
  readonly avatarStyle = computed(() => {
    const color = this.visits.technician()?.colorHex ?? null;
    return color === null ? null : { background: color, color: avatarTextColor(color) };
  });

  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.path.set(this.currentPath());
        this.pageTitle.set(this.titleFor());
        this.openPanel.set(null);
      });
  }

  isCurrent(match: (path: string) => boolean): boolean {
    return match(this.path());
  }

  togglePanel(kind: PanelKind): void {
    this.openPanel.set(this.openPanel() === kind ? null : kind);
  }

  closePanel(): void {
    this.openPanel.set(null);
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (
      this.openPanel() !== null &&
      target instanceof Element &&
      target.closest('[data-shell-panel]') === null
    ) {
      this.closePanel();
    }
  }

  /** Same flow as the desktop shell: keep the session on failure and tell the user. */
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
    this.closePanel();
  }

  private currentPath(): string {
    return this.router.url.split(/[?#]/)[0];
  }

  private titleFor(): string {
    const leaf = deepest(this.router.routerState.snapshot.root);
    const shellTitle = leaf.data['shellTitle'];
    if (typeof shellTitle === 'string') {
      return shellTitle;
    }
    return comingSoonModuleName(leaf.paramMap.get('module') ?? '') ?? '';
  }
}
