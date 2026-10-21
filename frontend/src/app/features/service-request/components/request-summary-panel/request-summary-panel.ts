import { Component, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { WizardStep } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { StepIcon } from '../step-icon/step-icon';

import { localizedList, t } from '../../../../core/i18n/translate';
@Component({
  selector: 'app-request-summary-panel',
  imports: [TranslocoPipe, ButtonDirective, StepIcon],
  templateUrl: './request-summary-panel.html',
  styleUrl: './request-summary-panel.scss',
})
export class RequestSummaryPanel {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly before = localizedList(
    () =>
      [
        {
          icon: 'pi-file-edit',
          title: t('serviceRequest.requestSummaryPanel.wellReviewYourRequest'),
          text: t('serviceRequest.requestSummaryPanel.ourTeamWillLook'),
        },
        {
          icon: 'pi-phone',
          title: t('serviceRequest.requestSummaryPanel.weMayContactYou'),
          text: t('serviceRequest.requestSummaryPanel.weMightReachOut'),
        },
        {
          icon: 'pi-bell',
          title: t('serviceRequest.requestSummaryPanel.youllReceiveUpdates'),
          text: t('serviceRequest.requestSummaryPanel.wellLetYouKnow'),
        },
      ] as const,
  );
  protected readonly icons: Record<WizardStep, string> = {
    contact: 'pi-user',
    property: 'pi-home',
    service: 'pi-wrench',
    availability: 'pi-calendar',
    review: 'pi-check-circle',
  };
}
