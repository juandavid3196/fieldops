import { Pipe, PipeTransform } from '@angular/core';

import { activeLanguageCode, hasTranslation, t } from './translate';

/**
 * BR-14: label of a stable code. `{{ role.name | codeLabel: 'roles' : role.code }}` shows
 * `roles.<code>` in the active language and falls back to the server name (or stored text) for an
 * unknown code, without a missing-key warning.
 */
@Pipe({ name: 'codeLabel', pure: false })
export class CodeLabelPipe implements PipeTransform {
  transform(
    fallback: string | null | undefined,
    prefix: string,
    code: string | null | undefined,
  ): string {
    activeLanguageCode();
    const key = `${prefix}.${code ?? ''}`;
    return code && hasTranslation(key) ? t(key) : (fallback ?? '');
  }
}
