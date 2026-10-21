import { Component, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { TranslocoPipe } from '@jsverse/transloco';

import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

@Component({
  selector: 'app-contact-step',
  imports: [TranslocoPipe, ButtonDirective, InputText],
  templateUrl: './contact-step.html',
  styleUrl: './contact-step.scss',
})
export class ContactStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly contact = () => this.store.data().contact;
}
