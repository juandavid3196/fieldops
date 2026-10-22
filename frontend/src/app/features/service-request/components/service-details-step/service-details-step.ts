import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { TranslocoPipe } from '@jsverse/transloco';

import { Urgency } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { NOT_SURE_LABEL } from '../../service-request.messages';
import { AttachmentDropzone } from '../attachment-dropzone/attachment-dropzone';

import { localizedList, t } from '../../../../core/i18n/translate';
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
  imports: [TranslocoPipe, FormsModule, ButtonDirective, Select, Textarea, AttachmentDropzone],
  templateUrl: './service-details-step.html',
  styleUrl: './service-details-step.scss',
})
export class ServiceDetailsStep {
  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly service = computed(() => this.store.data().service);
  protected get notSureLabel(): string {
    return NOT_SURE_LABEL();
  }
  protected readonly categoryIcon = categoryIcon;
  // The select sits inside a <form>; without `standalone` ngModel throws NG01352 while the
  // step renders, leaving the whole step half-rendered until the next change detection.
  protected readonly standalone = { standalone: true };
  protected readonly urgencies: readonly { value: Urgency; label: string; hint: string }[] =
    localizedList(() => [
      {
        value: 'standard',
        label: t('serviceRequest.serviceDetailsStep.standard'),
        hint: t('serviceRequest.serviceDetailsStep.routineIssue'),
      },
      {
        value: 'urgent',
        label: t('serviceRequest.serviceDetailsStep.urgent'),
        hint: t('serviceRequest.serviceDetailsStep.needsPromptAttention'),
      },
      {
        value: 'emergency',
        label: t('serviceRequest.serviceDetailsStep.emergency'),
        hint: t('serviceRequest.serviceDetailsStep.safetyRiskOrMajor'),
      },
    ]);

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
