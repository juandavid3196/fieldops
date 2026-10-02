import { Component, computed, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';

import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

@Component({
  selector: 'app-review-step',
  imports: [ButtonDirective, Message],
  templateUrl: './review-step.html',
  styleUrl: './review-step.scss',
})
export class ReviewStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly service = computed(() => this.store.data().service);
  protected readonly icons = {
    contact: 'pi-user',
    property: 'pi-home',
    service: 'pi-wrench',
    availability: 'pi-calendar',
  } as const;
}
