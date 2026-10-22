import { Component, inject } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';

@Component({
  selector: 'app-confirmation',
  imports: [TranslocoPipe, ButtonDirective],
  templateUrl: './confirmation.html',
  styleUrl: './confirmation.scss',
})
export class Confirmation {
  protected readonly store = inject(ServiceRequestWizardStore);
}
