import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { TranslocoPipe } from '@jsverse/transloco';

import { NEW_PROPERTY_CHOICE, PropertyType } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { US_STATES } from '../../service-request.validators';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
@Component({
  selector: 'app-property-step',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    ButtonDirective,
    InputText,
    Select,
    Textarea,
  ],
  templateUrl: './property-step.html',
  styleUrl: './property-step.scss',
})
export class PropertyStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly property = () => this.store.data().property;
  protected readonly states = [...US_STATES];
  protected readonly newChoice = NEW_PROPERTY_CHOICE;
  // The select sits inside a <form>; without `standalone` ngModel throws NG01352 while the
  // step renders, leaving the whole step half-rendered until the next change detection.
  protected readonly standalone = { standalone: true };
  protected readonly types: readonly { value: PropertyType; label: string; icon: string }[] =
    localizedList(() => [
      { value: 'home', label: t('serviceRequest.propertyStep.home'), icon: 'pi-home' },
      { value: 'business', label: t('serviceRequest.propertyStep.business'), icon: 'pi-building' },
    ]);

  protected chooseState(state: string): void {
    this.store.patchProperty({ state });
    this.store.revalidate('property.state');
  }
}
