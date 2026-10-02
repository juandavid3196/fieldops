import { Component, computed, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';

import { DateMode, TimeWindow } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { MAX_ADVANCE_DAYS, addDays } from '../../service-request.validators';

@Component({
  selector: 'app-availability-step',
  imports: [ButtonDirective, InputText, Textarea],
  templateUrl: './availability-step.html',
  styleUrl: './availability-step.scss',
})
export class AvailabilityStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly availability = computed(() => this.store.data().availability);
  protected readonly minDate = computed(() => this.store.today());
  protected readonly maxDate = computed(() => addDays(this.store.today(), MAX_ADVANCE_DAYS));
  protected readonly modes: readonly { value: DateMode; label: string; icon: string }[] = [
    { value: 'asap', label: 'As soon as possible', icon: 'pi-calendar' },
    { value: 'date', label: 'Choose a date', icon: 'pi-calendar' },
    { value: 'flexible', label: "I'm flexible", icon: 'pi-calendar-plus' },
  ];
  protected readonly windows: readonly {
    value: TimeWindow;
    label: string;
    range: string;
    icon: string;
  }[] = [
    { value: 'morning', label: 'Morning', range: '8 AM – 12 PM', icon: 'pi-sun' },
    { value: 'afternoon', label: 'Afternoon', range: '12 – 5 PM', icon: 'pi-sun' },
    { value: 'evening', label: 'Evening', range: '5 – 8 PM', icon: 'pi-moon' },
    { value: 'any', label: 'Any time', range: '', icon: 'pi-clock' },
  ];

  protected chooseMode(dateMode: DateMode): void {
    this.store.patchAvailability({ dateMode });
    this.store.revalidate('availability.preferredDate');
  }
}
