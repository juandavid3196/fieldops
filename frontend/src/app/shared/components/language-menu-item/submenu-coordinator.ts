import { Injectable, signal } from '@angular/core';

/**
 * Tracks which shell-menu sublist (Language or Appearance) is open, so opening one closes the
 * other instead of leaving both flyouts on screen.
 */
@Injectable({ providedIn: 'root' })
export class SubmenuCoordinator {
  readonly active = signal<object | null>(null);
}
