import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { WizardStep } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

import { localizedList, t } from '../../../../core/i18n/translate';
@Component({
  selector: 'app-request-stepper',
  imports: [TranslocoPipe],
  templateUrl: './request-stepper.html',
  styleUrl: './request-stepper.scss',
})
export class RequestStepper {
  /** Marks every step as completed (confirmation page). */
  readonly complete = input(false);
  protected readonly store = inject(ServiceRequestWizardStore);
  private readonly allSteps: readonly { id: WizardStep; label: string }[] = localizedList(() => [
    { id: 'contact', label: t('serviceRequest.requestStepper.contact') },
    { id: 'property', label: t('serviceRequest.requestStepper.property') },
    { id: 'service', label: t('serviceRequest.requestStepper.serviceDetails') },
    { id: 'availability', label: t('serviceRequest.requestStepper.availability') },
    { id: 'review', label: t('serviceRequest.requestStepper.review') },
  ]);
  /** The portal wizard has no Contact step. */
  protected readonly steps = computed(() => {
    const order = this.store.order();
    return this.allSteps.filter((step) => order.includes(step.id));
  });

  protected isDone(step: WizardStep): boolean {
    return this.complete() || this.store.completed().has(step);
  }

  protected isCurrent(step: WizardStep): boolean {
    return !this.complete() && this.store.step() === step;
  }

  /** Only completed, non-current steps are links; never on the confirmation page or while sending. */
  protected isNavigable(step: WizardStep): boolean {
    return !this.complete() && this.store.completed().has(step) && this.store.step() !== step;
  }

  protected open(step: WizardStep): void {
    if (this.store.submitting() || !this.isNavigable(step)) return;
    this.store.edit(step);
  }
}
