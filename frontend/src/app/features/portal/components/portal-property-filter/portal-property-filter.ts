import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Select } from 'primeng/select';

import { propertyLabel } from '../../utils/portal-format';

export interface PropertyOption {
  readonly id: string;
  readonly name: string;
  readonly addressLine1: string;
}

interface SelectItem {
  readonly label: string;
  readonly value: string;
}

/** Sentinel of "All properties": a null option value reads as no selection in the select. */
const ALL = 'all';

/**
 * Property selector of Home and the module lists (BR-18): "<name> — <address line 1>" options,
 * "All properties" first when `showAll`. `null` means All. Selection state lives in the page.
 */
@Component({
  selector: 'app-portal-property-filter',
  imports: [FormsModule, Select],
  template: `
    <label class="filter__label" [for]="inputId">{{ label() }}</label>
    <p-select
      [inputId]="inputId"
      [options]="items()"
      optionLabel="label"
      optionValue="value"
      appendTo="body"
      fluid
      [ngModel]="value() ?? allValue"
      [ariaLabel]="label()"
      (onChange)="changed.emit($event.value === allValue ? null : $event.value)"
    >
      <ng-template #selectedItem let-item>
        <span class="filter__selected"
          ><i class="pi pi-home" aria-hidden="true"></i>{{ item.label }}</span
        >
      </ng-template>
    </p-select>
  `,
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }

    .filter__label {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .filter__selected {
      display: inline-flex;
      gap: 0.5rem;
      align-items: center;
    }
  `,
})
export class PortalPropertyFilter {
  readonly properties = input.required<readonly PropertyOption[]>();
  /** Selected property id; `null` is "All properties". */
  readonly value = input<string | null>(null);
  readonly showAll = input(true);
  readonly label = input('Property');
  readonly inputId = `portal-property-${Math.random().toString(36).slice(2, 8)}`;

  readonly changed = output<string | null>();
  protected readonly allValue = ALL;

  protected readonly items = computed<SelectItem[]>(() => {
    const options = this.properties().map((property) => ({
      label: propertyLabel(property),
      value: property.id,
    }));
    return this.showAll() ? [{ label: 'All properties', value: ALL }, ...options] : options;
  });
}
