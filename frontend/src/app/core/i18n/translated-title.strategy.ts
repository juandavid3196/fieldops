import { Injectable, Injector, effect, inject, runInInjectionContext, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import {
  ActivatedRouteSnapshot,
  ResolveFn,
  RouterStateSnapshot,
  TitleStrategy,
} from '@angular/router';

import { I18n } from './i18n.service';

function deepest(route: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let current = route;
  while (current.firstChild !== null) {
    current = current.firstChild;
  }
  return current;
}

/**
 * BR-19: route titles are translation keys (`titles.<name>`); the document title is resolved from
 * the active language and re-resolved on a language switch. A title function (built from loaded
 * data) is run again with the new language; any other title is used as given.
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18n);
  private readonly injector = inject(Injector);
  private readonly current = signal<RouterStateSnapshot | undefined>(undefined);

  constructor() {
    super();
    effect(() => {
      const snapshot = this.current();
      this.i18n.language();
      if (snapshot === undefined) {
        return;
      }
      const raw = this.rawTitle(snapshot);
      if (raw !== undefined) {
        this.title.setTitle(this.i18n.has(raw) ? this.i18n.t(raw) : raw);
      }
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.current.set(snapshot);
  }

  private rawTitle(snapshot: RouterStateSnapshot): string | undefined {
    const leaf = deepest(snapshot.root);
    const config = leaf.routeConfig?.title;
    if (typeof config === 'function') {
      try {
        const value = runInInjectionContext(this.injector, () =>
          (config as ResolveFn<string>)(leaf, snapshot),
        );
        if (typeof value === 'string') {
          return value;
        }
      } catch {
        // Fall back to the title resolved at navigation time.
      }
    }
    return this.buildTitle(snapshot);
  }
}
