import { EnvironmentProviders, Injectable, Provider, inject } from '@angular/core';
import { CanActivateFn, Route } from '@angular/router';
import { InlineLoader, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';

import { LanguageCode } from './languages.config';
import { I18N_SCOPE_DEPENDENCIES } from './scope-dependencies';

/** `technician-jobs` -> `technicianJobs`: the key prefix of a feature scope. */
export function scopeIdOf(folder: string): string {
  return folder.replace(/-([a-z0-9])/g, (_, letter: string) => letter.toUpperCase());
}

/**
 * Lazy loaders of `features/<folder>/i18n/<lang>.json`. The build splits every JSON file into its
 * own chunk, so a scope is downloaded only when one of its routes activates (BR-20).
 */
export function scopeLoader(folder: string): InlineLoader {
  return {
    en: () => import(`../../features/${folder}/i18n/en.json`),
    es: () => import(`../../features/${folder}/i18n/es.json`),
  };
}

function inlineLoaderOf(folder: string): InlineLoader {
  const scope = scopeIdOf(folder);
  const loader = scopeLoader(folder);
  return Object.fromEntries(
    Object.entries(loader).map(([lang, load]) => [`${scope}/${lang}`, load]),
  );
}

/** Scopes activated so far, so a language switch reloads each of them for the new language. */
@Injectable({ providedIn: 'root' })
export class I18nScopeRegistry {
  private readonly transloco = inject(TranslocoService);
  private readonly folders = new Set<string>();

  register(folder: string): void {
    this.folders.add(folder);
  }

  /** Loads one scope for `lang`; a failure leaves English (BR-11) and never throws. */
  load(folder: string, lang: string): Observable<unknown> {
    return this.transloco
      .load(`${scopeIdOf(folder)}/${lang}`, { inlineLoader: inlineLoaderOf(folder) })
      .pipe(catchError(() => of(null)));
  }

  /** Reload of every activated scope for the new language. */
  loadAll(lang: LanguageCode): Observable<unknown> {
    const loads = [...this.folders].map((folder) => this.load(folder, lang));
    return loads.length === 0 ? of(null) : forkJoin(loads);
  }
}

/** The folders plus every scope they depend on (`scope-dependencies.ts`), without repeats. */
export function scopeClosure(folders: readonly string[]): string[] {
  const seen = new Set<string>();
  const visit = (folder: string): void => {
    if (seen.has(folder)) {
      return;
    }
    seen.add(folder);
    (I18N_SCOPE_DEPENDENCIES[folder] ?? []).forEach(visit);
  };
  folders.forEach(visit);
  return [...seen];
}

/** Guard that loads the scopes of a route for the active language before it activates. */
export function i18nScopeGuard(...routeFolders: readonly string[]): CanActivateFn {
  const folders = scopeClosure(routeFolders);
  return () => {
    const registry = inject(I18nScopeRegistry);
    const transloco = inject(TranslocoService);
    for (const folder of folders) {
      registry.register(folder);
    }
    const lang = transloco.getActiveLang();
    return forkJoin(folders.map((folder) => registry.load(folder, lang))).pipe(map(() => true));
  };
}

/** Providers of the scopes of one route (`provideTranslocoScope`, BR-20). */
export function i18nScopeProviders(
  ...folders: readonly string[]
): (Provider | EnvironmentProviders)[] {
  return folders.flatMap((folder) =>
    provideTranslocoScope({ scope: scopeIdOf(folder), loader: scopeLoader(folder) }),
  );
}

/**
 * Adds the feature scopes to a route: `provideTranslocoScope` for the pipes and a guard that has
 * the scopes loaded before the route renders. `folders` are `features/<folder>` names.
 */
export function withI18n(route: Route, ...folders: readonly string[]): Route {
  return {
    ...route,
    providers: [...i18nScopeProviders(...folders), ...(route.providers ?? [])],
    canActivate: [i18nScopeGuard(...folders), ...(route.canActivate ?? [])],
  };
}
