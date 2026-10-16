import { Injectable, computed, signal } from '@angular/core';

import { ReportKey } from '../models/reports.model';
import { isReportKey } from '../utils/report-catalog';

export const MAX_RECENT_REPORTS = 5;

interface Stored {
  readonly favorites: readonly ReportKey[];
  readonly recent: readonly ReportKey[];
}

/**
 * BR-18: Favorites and Recently viewed live only in `localStorage`, under a key scoped to the
 * organization and user. Any storage failure leaves both lists empty; nothing reaches the server.
 */
@Injectable()
export class ReportPreferences {
  private storageKey: string | null = null;
  private readonly stored = signal<Stored>({ favorites: [], recent: [] });
  private readonly visible = signal<readonly ReportKey[]>([]);

  /** Ordered favorites the caller can see. */
  readonly favorites = computed(() => this.stored().favorites.filter((key) => this.isVisible(key)));
  /** Last distinct reports viewed, newest first, limited to those the caller can see. */
  readonly recent = computed(() => this.stored().recent.filter((key) => this.isVisible(key)));

  private isVisible(key: ReportKey): boolean {
    return this.visible().includes(key);
  }

  /** Reads the stored lists for this member once the session and the visible reports are known. */
  load(organizationId: string, userId: string, visible: readonly ReportKey[]): void {
    this.visible.set(visible);
    const key = `fieldops.reports.${organizationId}.${userId}`;
    if (key === this.storageKey) {
      return;
    }
    this.storageKey = key;
    try {
      const raw = localStorage.getItem(key);
      const parsed: unknown = raw === null ? {} : JSON.parse(raw);
      const read = (name: 'favorites' | 'recent'): ReportKey[] => {
        const list = (parsed as Record<string, unknown>)[name];
        return Array.isArray(list)
          ? [...new Set(list.filter((item): item is ReportKey => isReportKey(String(item))))]
          : [];
      };
      this.stored.set({ favorites: read('favorites'), recent: read('recent') });
    } catch {
      this.stored.set({ favorites: [], recent: [] });
    }
  }

  isFavorite(key: ReportKey): boolean {
    return this.favorites().includes(key);
  }

  toggleFavorite(key: ReportKey): void {
    const current = this.stored().favorites;
    this.save({
      ...this.stored(),
      favorites: current.includes(key) ? current.filter((item) => item !== key) : [...current, key],
    });
  }

  /** Called only after a report loaded successfully. */
  markViewed(key: ReportKey): void {
    const current = this.stored();
    if (current.recent[0] === key) {
      return;
    }
    this.save({
      ...current,
      recent: [key, ...current.recent.filter((item) => item !== key)].slice(0, MAX_RECENT_REPORTS),
    });
  }

  private save(next: Stored): void {
    this.stored.set(next);
    if (this.storageKey === null) {
      return;
    }
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(next));
    } catch {
      this.stored.set({ favorites: [], recent: [] });
    }
  }
}
