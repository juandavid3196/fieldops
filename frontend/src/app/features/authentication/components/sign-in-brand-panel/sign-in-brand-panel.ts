import { NgOptimizedImage } from '@angular/common';
import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { localizedList, t } from '../../../../core/i18n/translate';
interface BrandItem {
  readonly title: string;
  readonly text: string;
  readonly icon: string;
}

interface StatusCard extends BrandItem {
  readonly tone: 'green' | 'blue' | 'purple';
}

/**
 * Decorative brand panel of the Sign In page (design 26); shown from `lg`.
 *
 * The photo stays lazy (no `priority`) so it is never downloaded below `lg`, where the
 * panel is hidden. From `lg` it is the LCP element, so dev builds log NG02955; Angular
 * has no supported per-image opt-out for that dev-only check, and `priority` would
 * preload the photo on mobile too. Accepted trade-off (sign-in spec, audit F-05).
 */
@Component({
  selector: 'app-sign-in-brand-panel',
  imports: [TranslocoPipe, NgOptimizedImage],
  templateUrl: './sign-in-brand-panel.html',
  styleUrl: './sign-in-brand-panel.scss',
})
export class SignInBrandPanel {
  readonly statusCards: readonly StatusCard[] = localizedList(() => [
    {
      title: t('authentication.signInBrandPanel.requestApproved'),
      text: t('authentication.signInBrandPanel.customerRequestInReady'),
      icon: 'pi-check',
      tone: 'green',
    },
    {
      title: t('authentication.signInBrandPanel.technicianAssigned'),
      text: t('authentication.signInBrandPanel.theRightPersonFor'),
      icon: 'pi-user',
      tone: 'blue',
    },
    {
      title: t('authentication.signInBrandPanel.invoicePaid'),
      text: t('authentication.signInBrandPanel.workCompletePaymentReceived'),
      icon: 'pi-file',
      tone: 'purple',
    },
  ]);

  readonly benefits: readonly BrandItem[] = localizedList(() => [
    {
      title: t('authentication.signInBrandPanel.workRunsSmoother'),
      text: t('authentication.signInBrandPanel.fromRequestsToPayments'),
      icon: 'pi-calendar',
    },
    {
      title: t('authentication.signInBrandPanel.happierCustomers'),
      text: t('authentication.signInBrandPanel.fasterResponseTimesAnd'),
      icon: 'pi-users',
    },
    {
      title: t('authentication.signInBrandPanel.aMoreProfitableBusiness'),
      text: t('authentication.signInBrandPanel.lessAdminWorkMore'),
      icon: 'pi-chart-bar',
    },
  ]);
}
