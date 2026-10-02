import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';

import { Urgency } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { NOT_SURE_LABEL } from '../../service-request.messages';
import { AttachmentDropzone } from '../attachment-dropzone/attachment-dropzone';

/** Categories are tenant data (names only), so the icon is picked by keyword with a fallback. */
const CATEGORY_ICONS: readonly (readonly [RegExp, string])[] = [
  [/plumb|pipe|drain|water/i, 'pi-link'],
  [/hvac|heat|cool|air|furnace/i, 'pi-cloud'],
  [/electr|wiring|power|light/i, 'pi-bolt'],
  [/clean|janitor|maid/i, 'pi-sparkles'],
  [/landscap|garden|lawn|tree|yard/i, 'pi-globe'],
  [/general|maintenance|handyman|repair/i, 'pi-wrench'],
];

export function categoryIcon(name: string): string {
  return CATEGORY_ICONS.find(([pattern]) => pattern.test(name))?.[1] ?? 'pi-th-large';
}

@Component({
  selector: 'app-service-details-step',
  imports: [FormsModule, ButtonDirective, Select, Textarea, AttachmentDropzone],
  templateUrl: './service-details-step.html',
  styleUrl: './service-details-step.scss',
})
export class ServiceDetailsStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly service = computed(() => this.store.data().service);
  protected readonly notSureLabel = NOT_SURE_LABEL;
  protected readonly categoryIcon = categoryIcon;
  // The select sits inside a <form>; without `standalone` ngModel throws NG01352 while the
  // step renders, leaving the whole step half-rendered until the next change detection.
  protected readonly standalone = { standalone: true };
  protected readonly urgencies: readonly { value: Urgency; label: string; hint: string }[] = [
    { value: 'standard', label: 'Standard', hint: 'Routine issue' },
    { value: 'urgent', label: 'Urgent', hint: 'Needs prompt attention' },
    { value: 'emergency', label: 'Emergency', hint: 'Safety risk or major damage' },
  ];

  protected chooseCategory(categoryId: string): void {
    this.store.patchService({ categoryId });
    this.store.revalidate('service.categoryId');
    this.store.revalidate('service.serviceId');
  }

  protected chooseService(serviceId: string): void {
    this.store.patchService({ serviceId });
    this.store.revalidate('service.serviceId');
  }

  protected toggleNotSure(notSure: boolean): void {
    this.store.patchService({ notSure });
    this.store.revalidate('service.serviceId');
  }
}
