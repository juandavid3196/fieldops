import { Component, computed, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { TranslocoPipe } from '@jsverse/transloco';

import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { StepIcon } from '../step-icon/step-icon';

@Component({
  selector: 'app-review-step',
  imports: [TranslocoPipe, ButtonDirective, Message, StepIcon],
  templateUrl: './review-step.html',
  styleUrl: './review-step.scss',
})
export class ReviewStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly service = computed(() => this.store.data().service);
}
