import { Component, computed, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';
import { TranslocoPipe } from '@jsverse/transloco';

import { DateMode, TimeWindow } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { MAX_ADVANCE_DAYS, addDays } from '../../service-request.validators';

import { localizedList, t } from '../../../../core/i18n/translate';
@Component({
  selector: 'app-availability-step',
  imports: [TranslocoPipe, ButtonDirective, InputText, Textarea],
  templateUrl: './availability-step.html',
  styleUrl: './availability-step.scss',
})
export class AvailabilityStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly availability = computed(() => this.store.data().availability);
  protected readonly minDate = computed(() => this.store.today());
  protected readonly maxDate = computed(() => addDays(this.store.today(), MAX_ADVANCE_DAYS));
  protected readonly modes: readonly { value: DateMode; label: string; icon: string }[] =
    localizedList(() => [
      {
        value: 'asap',
        label: t('serviceRequest.availabilityStep.asSoonAsPossible'),
        icon: 'pi-calendar',
      },
      {
        value: 'date',
        label: t('serviceRequest.availabilityStep.chooseADate'),
        icon: 'pi-calendar',
      },
      {
        value: 'flexible',
        label: t('serviceRequest.availabilityStep.imFlexible'),
        icon: 'pi-calendar-plus',
      },
    ]);
  protected readonly windows: readonly {
    value: TimeWindow;
    label: string;
    range: string;
    icon: string;
  }[] = localizedList(() => [
    {
      value: 'morning',
      label: t('serviceRequest.availabilityStep.morning'),
      range: t('serviceRequest.availabilityStep.n8Am12Pm'),
      icon: 'pi-sun',
    },
    {
      value: 'afternoon',
      label: t('serviceRequest.availabilityStep.afternoon'),
      range: t('serviceRequest.availabilityStep.n125Pm'),
      icon: 'pi-sun',
    },
    {
      value: 'evening',
      label: t('serviceRequest.availabilityStep.evening'),
      range: t('serviceRequest.availabilityStep.n58Pm'),
      icon: 'pi-moon',
    },
    {
      value: 'any',
      label: t('serviceRequest.availabilityStep.anyTime'),
      range: '',
      icon: 'pi-clock',
    },
  ]);

  protected chooseMode(dateMode: DateMode): void {
    this.store.patchAvailability({ dateMode });
    this.store.revalidate('availability.preferredDate');
  }
}
