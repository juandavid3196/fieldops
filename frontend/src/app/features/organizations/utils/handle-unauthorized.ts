import { Router } from '@angular/router';
import { WritableSignal } from '@angular/core';

/**
 * FR-19: on `401`, clears the session flag and navigates to Sign In before
 * anything else runs, so the FR-17 unsaved-changes guard and any dirty-drawer
 * close prompt are short-circuited (AC-50).
 */
export function handleUnauthorized(router: Router, sessionExpired: WritableSignal<boolean>): void {
  sessionExpired.set(true);
  void router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
}
