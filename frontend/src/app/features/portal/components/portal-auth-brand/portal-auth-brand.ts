import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { localizedList, t } from '../../../../core/i18n/translate';
/** Decorative left panel of the portal sign-in: wordmark, headline and floating module cards. */
@Component({
  selector: 'app-portal-auth-brand',
  imports: [TranslocoPipe],
  templateUrl: './portal-auth-brand.html',
  styleUrl: './portal-auth-brand.scss',
})
export class PortalAuthBrand {
  protected readonly cards = localizedList(
    () =>
      [
        { name: t('portal.portalAuthBrand.quotes'), icon: 'pi-check', tone: 'teal' },
        { name: t('portal.portalAuthBrand.appointments'), icon: 'pi-calendar', tone: 'blue' },
        { name: t('portal.portalAuthBrand.invoices'), icon: 'pi-file', tone: 'teal' },
      ] as const,
  );
}
