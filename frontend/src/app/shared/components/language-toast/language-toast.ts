import { Component, effect, inject, untracked } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Toast } from 'primeng/toast';

import { LanguagePreferenceService } from '../../../core/i18n/language-preference.service';
import { t } from '../../../core/i18n/translate';

/**
 * Shell toast for a language save that failed (BR-09). The text is resolved after the previous
 * language was restored, so it is shown in the restored language.
 */
@Component({
  selector: 'app-language-toast',
  imports: [Toast],
  providers: [MessageService],
  template: '<p-toast />',
})
export class LanguageToast {
  private readonly messages = inject(MessageService);
  private readonly preference = inject(LanguagePreferenceService);
  private shown = this.preference.failures();

  constructor() {
    effect(() => {
      const failures = this.preference.failures();
      untracked(() => {
        if (failures !== this.shown) {
          this.shown = failures;
          this.messages.add({ severity: 'error', summary: t('selector.saveFailed'), life: 6000 });
        }
      });
    });
  }
}
