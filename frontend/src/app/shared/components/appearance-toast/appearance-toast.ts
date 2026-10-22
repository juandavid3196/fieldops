import { Component, effect, inject, untracked } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Toast } from 'primeng/toast';

import { t } from '../../../core/i18n/translate';
import { ThemePreferenceService } from '../../../core/theme/theme-preference.service';

/** Shell toast for an appearance save that failed and was restored (BR-15). */
@Component({
  selector: 'app-appearance-toast',
  imports: [Toast],
  providers: [MessageService],
  template: '<p-toast />',
})
export class AppearanceToast {
  private readonly messages = inject(MessageService);
  private readonly preference = inject(ThemePreferenceService);
  private shown = this.preference.failures();

  constructor() {
    effect(() => {
      const failures = this.preference.failures();
      untracked(() => {
        if (failures !== this.shown) {
          this.shown = failures;
          this.messages.add({
            severity: 'error',
            summary: t('selector.appearanceSaveFailed'),
            life: 6000,
          });
        }
      });
    });
  }
}
