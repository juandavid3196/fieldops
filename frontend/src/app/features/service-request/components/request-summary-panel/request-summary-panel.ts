import { Component, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { WizardStep } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

@Component({
  selector: 'app-request-summary-panel',
  imports: [ButtonDirective],
  templateUrl: './request-summary-panel.html',
  styleUrl: './request-summary-panel.scss',
})
export class RequestSummaryPanel {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly before = [
    {
      icon: 'pi-file-edit',
      title: "We'll review your request",
      text: 'Our team will look over the details and confirm we have everything we need.',
    },
    {
      icon: 'pi-phone',
      title: 'We may contact you',
      text: 'We might reach out if we have questions or need additional information.',
    },
    {
      icon: 'pi-bell',
      title: "You'll receive updates",
      text: "We'll let you know the next steps by email or phone once your request is reviewed.",
    },
  ] as const;
  protected readonly icons: Record<WizardStep, string> = {
    contact: 'pi-user',
    property: 'pi-home',
    service: 'pi-wrench',
    availability: 'pi-calendar',
    review: 'pi-check-circle',
  };
}
