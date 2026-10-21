import { Injectable } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/** Root scope loader (BR-20): shell, navigation, common actions, errors, enums, glossary. */
@Injectable({ providedIn: 'root' })
export class RootTranslationLoader implements TranslocoLoader {
  getTranslation(lang: string): Promise<Translation> {
    switch (lang) {
      case 'es':
        return import('./es.json').then((module) => module.default);
      default:
        return import('./en.json').then((module) => module.default);
    }
  }
}
