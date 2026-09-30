import { Injectable } from '@angular/core';

const STORAGE_KEY = 'fieldops.password-reset-token';

/**
 * Tab-scoped password reset token (BR-20). It is written from the URL fragment,
 * read only from here and cleared when the reset is consumed or unusable.
 * An in-memory copy keeps the flow working if `sessionStorage` is unavailable.
 */
@Injectable({ providedIn: 'root' })
export class PasswordResetTokenService {
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
