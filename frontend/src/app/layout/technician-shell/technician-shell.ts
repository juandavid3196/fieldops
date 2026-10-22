import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  Renderer2,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
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
import { TranslocoPipe } from '@jsverse/transloco';

import { comingSoonModuleName } from '../../core/config/coming-soon-modules';
import { MessageUnreadService } from '../../core/services/message-unread.service';
import { SessionService } from '../../core/services/session.service';
import { TechnicianVisitsService } from '../../features/technician-jobs/services/technician-visits.service';
import { AppearanceMenuItem } from '../../shared/components/appearance-menu-item/appearance-menu-item';
import { AppearanceToast } from '../../shared/components/appearance-toast/appearance-toast';
import { LanguageMenuItem } from '../../shared/components/language-menu-item/language-menu-item';
import { LanguageToast } from '../../shared/components/language-toast/language-toast';
import { UserAvatar } from '../../shared/components/user-avatar/user-avatar';
import { avatarTextColor } from '../../features/technician-jobs/utils/technician-format';
import { ACCOUNT_LINK, HELP_LINK, PROFILE_LINK, TECHNICIAN_NAV } from './technician-shell.nav';

import { t, tOrRaw } from '../../core/i18n/translate';
export interface ShellBack {
  readonly link: string;
  readonly label: string;
}

export const SIGN_OUT_ERROR_MESSAGE = (): string =>
  t('technicianJobs.technicianShell.signOutErrorMessage');
type PanelKind = 'more';
/** Read by the `wide` breakpoint mixin (styles/_breakpoints.scss). */
const PHONE_SHELL_CLASS = 'fo-phone-shell';

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
  imports: [
    TranslocoPipe,
    AppearanceMenuItem,
    AppearanceToast,
    LanguageMenuItem,
    LanguageToast,
    RouterLink,
    RouterOutlet,
    Message,
    UserAvatar,
  ],
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
  private readonly unread = inject(MessageUnreadService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly moreButton = viewChild<ElementRef<HTMLButtonElement>>('moreButton');

  readonly nav = TECHNICIAN_NAV;
  readonly profileLink = PROFILE_LINK;
  readonly accountLink = ACCOUNT_LINK;
  readonly helpLink = HELP_LINK;
  get signOutErrorMessage(): string {
    return SIGN_OUT_ERROR_MESSAGE();
  }

  readonly openPanel = signal<PanelKind | null>(null);
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);
  private readonly path = signal(this.currentPath());
  private readonly routeTitle = signal(this.titleFor());
  /** The job page overrides the route title while it shows Job in progress (BR-20). */
  readonly pageTitle = computed(() => this.visits.pageTitle() ?? tOrRaw(this.routeTitle()));
  private readonly backTarget = signal(this.backFor());
  readonly back = computed(() => {
    const target = this.backTarget();
    return target === null ? null : { ...target, label: tOrRaw(target.label) };
  });
  /** Review & complete top bar (back action, number, status chip, labor time); `null` elsewhere. */
  readonly header = this.visits.shellHeader;
  /** Job page step progress drawn on the bottom nav's top edge; `null` elsewhere. */
  readonly jobProgress = this.visits.jobProgress;

  /** technician-messages BR-15: the badge of the Messages tab; `null` hides it. Technicians have
   * no notifications (the API forbids them), so there is no bell. */
  readonly unreadBadge = this.unread.badge;
  readonly unreadLabel = this.unread.label;

  readonly moreOpen = computed(() => this.openPanel() === 'more');
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
  /** The account photo (account-settings BR-08); the initials show while there is none. */
  readonly photoUpdatedAt = computed(
    () => this.sessionService.session()?.user.photoUpdatedAt ?? null,
  );
  readonly avatarStyle = computed(() => {
    const color = this.visits.technician()?.colorHex ?? null;
    return color === null ? null : { background: color, color: avatarTextColor(color) };
  });

  constructor() {
    // Pages shared with the app shell keep their phone layout here (`bp.wide` in styles).
    const body = inject(DOCUMENT).body;
    const renderer = inject(Renderer2);
    renderer.addClass(body, PHONE_SHELL_CLASS);
    this.destroyRef.onDestroy(() => renderer.removeClass(body, PHONE_SHELL_CLASS));

    // BR-14: the summary loads now and every 30 s while the tab is visible (technicians only).
    this.unread.reset();
    if (this.sessionService.session()?.role.code === 'technician') {
      this.unread.startPolling();
    }
    this.destroyRef.onDestroy(() => this.unread.reset());
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.path.set(this.currentPath());
        this.routeTitle.set(this.titleFor());
        this.backTarget.set(this.backFor());
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

  /** BR-18: choosing a language closes the panel and returns focus to the More button. */
  onLanguageChosen(): void {
    this.closePanel();
    afterNextRender(() => this.moreButton()?.nativeElement.focus(), { injector: this.injector });
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

  /** The deepest `data.shellTitle` on the route chain (mounted features set it on their parent). */
  private titleFor(): string {
    const leaf = deepest(this.router.routerState.snapshot.root);
    const shellTitle = leaf.pathFromRoot
      .map((snapshot) => snapshot.data['shellTitle'])
      .filter((title): title is string => typeof title === 'string')
      .at(-1);
    return shellTitle ?? comingSoonModuleName(leaf.paramMap.get('module') ?? '') ?? '';
  }

  /** `data.shellBack` replaces the logo with a back button (the job page). */
  private backFor(): ShellBack | null {
    const back: unknown = deepest(this.router.routerState.snapshot.root).data['shellBack'];
    return typeof back === 'object' &&
      back !== null &&
      'link' in back &&
      typeof back.link === 'string' &&
      'label' in back &&
      typeof back.label === 'string'
      ? { link: back.link, label: back.label }
      : null;
  }
}
