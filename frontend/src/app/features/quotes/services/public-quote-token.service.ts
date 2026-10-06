import { Injectable } from '@angular/core';

const STORAGE_KEY = 'fieldops.quote-link-token';

/**
 * Tab-scoped quote link token (BR-01), written from the URL fragment and only ever sent in POST
 * bodies. An in-memory copy keeps the page working if `sessionStorage` is unavailable.
 */
@Injectable({ providedIn: 'root' })
export class PublicQuoteTokenService {
  private memory: string | null = null;

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

  clear(): void {
    this.memory = null;
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Nothing stored.
    }
  }
}
