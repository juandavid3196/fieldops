import { Injectable } from '@angular/core';

const STORAGE_KEY = 'fieldops.invoice-link-token';
const ATTEMPT_KEY = 'fieldops.invoice-link-attempt';

/**
 * Tab-scoped invoice link token (BR-28), written from the URL fragment and only ever sent in POST
 * bodies. An in-memory copy keeps the page working if `sessionStorage` is unavailable. The card
 * `attemptId` (BR-29) is kept beside it so polling can resume after the Stripe redirect.
 */
@Injectable({ providedIn: 'root' })
export class PublicInvoiceTokenService {
  private memory: string | null = null;
  private attempt: string | null = null;

  /** Stores the `token` of a URL fragment such as `token=abc`; other fragments are ignored. */
  captureFromFragment(fragment: string | null): void {
    const token = new URLSearchParams(fragment ?? '').get('token')?.trim();
    if (token) {
      this.memory = token;
      try {
        sessionStorage.setItem(STORAGE_KEY, token);
      } catch {
        // Storage blocked: the in-memory copy is used.
      }
    }
  }

  read(): string | null {
    try {
      return sessionStorage.getItem(STORAGE_KEY) ?? this.memory;
    } catch {
      return this.memory;
    }
  }

  storeAttempt(attemptId: string): void {
    this.attempt = attemptId;
    try {
      sessionStorage.setItem(ATTEMPT_KEY, attemptId);
    } catch {
      // Storage blocked: the in-memory copy is used.
    }
  }

  readAttempt(): string | null {
    try {
      return sessionStorage.getItem(ATTEMPT_KEY) ?? this.attempt;
    } catch {
      return this.attempt;
    }
  }

  clearAttempt(): void {
    this.attempt = null;
    try {
      sessionStorage.removeItem(ATTEMPT_KEY);
    } catch {
      // Nothing stored.
    }
  }

  clear(): void {
    this.memory = null;
    this.clearAttempt();
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Nothing stored.
    }
  }
}
