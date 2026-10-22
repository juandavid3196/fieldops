import { Injectable } from '@angular/core';

import { hasTranslation, t } from './translate';

export interface FieldErrorEntry {
  /** Backend field key, or `*` for any field. */
  readonly field: string;
  /** Exact approved English backend message. */
  readonly message: string;
  /** Translation key of the localized text. */
  readonly key: string;
}

/**
 * BR-13: backend field messages are translated through a catalog keyed by field and the exact
 * approved English message. A miss yields the generic localized text; the backend text is never
 * shown. Features register their entries; the root registers the shared ones.
 */
@Injectable({ providedIn: 'root' })
export class FieldErrorCatalog {
  private readonly entries: FieldErrorEntry[] = [
    { field: '*', message: 'Choose a supported language.', key: 'errors.field.language' },
  ];

  register(...entries: readonly FieldErrorEntry[]): void {
    this.entries.push(...entries);
  }

  translate(field: string, message: string): string {
    const entry = this.entries.find(
      (candidate) =>
        candidate.message === message && (candidate.field === field || candidate.field === '*'),
    );
    return entry !== undefined && hasTranslation(entry.key)
      ? t(entry.key)
      : t('errors.field.generic');
  }
}
