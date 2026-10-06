import { Component, computed, input, output, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { SOURCE_LABELS } from '../../../jobs/utils/work-order-format';
import { MaterialRow } from '../../utils/work-order-form';
import {
  MaterialValue,
  WorkOrderMaterialDialog,
} from '../work-order-material-dialog/work-order-material-dialog';

export const MATERIALS_FOOTNOTE = 'Technician will record actual quantities used.';

export interface MaterialSave {
  /** `null` adds a new row. */
  readonly uid: string | null;
  readonly value: MaterialValue;
}

/** Required materials card (BR-12). Presentational: the page owns the rows. */
@Component({
  selector: 'app-work-order-materials',
  imports: [ButtonDirective, WorkOrderMaterialDialog],
  templateUrl: './work-order-materials.html',
  styleUrl: './work-order-materials.scss',
})
export class WorkOrderMaterials {
  readonly materials = input.required<readonly MaterialRow[]>();
  readonly errors = input.required<Readonly<Record<string, string>>>();
  readonly disabled = input(false);

  readonly saved = output<MaterialSave>();
  readonly removed = output<string>();

  readonly footnote = MATERIALS_FOOTNOTE;
  readonly sourceLabels = SOURCE_LABELS;
  readonly dialogOpen = signal(false);
  private readonly editingUid = signal<string | null>(null);
  readonly editing = computed(
    () => this.materials().find((material) => material.uid === this.editingUid()) ?? null,
  );

  error(material: MaterialRow): string | null {
    return this.errors()[`materials[${material.uid}]`] ?? null;
  }

  add(): void {
    this.editingUid.set(null);
    this.dialogOpen.set(true);
  }

  edit(material: MaterialRow): void {
    this.editingUid.set(material.uid);
    this.dialogOpen.set(true);
  }

  onSaved(value: MaterialValue): void {
    this.dialogOpen.set(false);
    this.saved.emit({ uid: this.editingUid(), value });
  }
}
