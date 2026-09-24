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
import { Router } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';

import { SessionService } from '../../../../core/services/session.service';

export const SIGN_OUT_ERROR_MESSAGE = "We couldn't sign you out. Try again.";

/** Minimal authenticated landing page (FR-17). */
@Component({
  selector: 'app-overview',
  imports: [ButtonDirective, Message, SpinnerIcon],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly signOutButton =
    viewChild.required<ElementRef<HTMLButtonElement>>('signOutButton');

  readonly session = this.sessionService.session;
  readonly heading = computed(() => {
    const firstName = this.session()?.user.firstName;
    return firstName ? `Welcome, ${firstName}` : 'Welcome';
  });
  readonly signingOut = signal(false);
  readonly signOutFailed = signal(false);
  readonly signOutErrorMessage = SIGN_OUT_ERROR_MESSAGE;

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
    afterNextRender(() => this.signOutButton().nativeElement.focus(), {
      injector: this.injector,
    });
  }
}
