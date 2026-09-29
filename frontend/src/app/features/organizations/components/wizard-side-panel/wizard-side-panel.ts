import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { STEP_LABELS, WizardStep } from '../../pages/register-company/register-company.helpers';

export interface StepperItem {
  readonly step: WizardStep;
  readonly label: string;
  readonly current: boolean;
  readonly completed: boolean;
  readonly selectable: boolean;
}

const STEPS: readonly WizardStep[] = [1, 2, 3, 4];

/**
 * Wizard side panel (FR-02): brand, heading, "Setup steps" stepper, security note and Sign in
 * link from `lg`; below `lg` it is the compact header with the step label and progress segments.
 */
@Component({
  selector: 'app-wizard-side-panel',
  imports: [RouterLink],
  templateUrl: './wizard-side-panel.html',
  styleUrl: './wizard-side-panel.scss',
})
export class WizardSidePanel {
  readonly current = input.required<WizardStep>();
  readonly completed = input.required<ReadonlySet<WizardStep>>();
  readonly reached = input.required<ReadonlySet<WizardStep>>();
  readonly locked = input(false);
  readonly stepSelected = output<WizardStep>();

  readonly steps = STEPS;
  readonly labels = STEP_LABELS;

  item(step: WizardStep): StepperItem {
    return {
      step,
      label: STEP_LABELS[step],
      current: this.current() === step,
      completed: this.completed().has(step),
      selectable: this.reached().has(step),
    };
  }

  accessibleName(item: StepperItem): string {
    return item.completed && !item.current ? `${item.label}, completed` : item.label;
  }
}
