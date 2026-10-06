import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MenuItem } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { CalculatedLine, LineType } from '../../models/quote.model';
import { QuoteLine, canMove, isLineValid, lineName } from '../../utils/quote-lines';
import {
  CatalogPickerDialog,
  PickedLine,
  PickerTarget,
} from '../catalog-picker-dialog/catalog-picker-dialog';
import { CalcStatus } from '../quote-summary/quote-summary';

export const EMPTY_LINES_MESSAGE = 'Add a service, material or optional item to build this quote.';

export interface LineChange {
  readonly uid: string;
  readonly patch: Partial<QuoteLine>;
}

export interface LineMove {
  readonly uid: string;
  readonly direction: -1 | 1;
}

export interface LineAdd extends PickedLine {
  readonly isOptional: boolean;
}

interface Row {
  readonly line: QuoteLine;
  /** Position in the whole list: names every input ("Line 3"). */
  readonly index: number;
}

interface TaxOption {
  readonly code: boolean;
  readonly label: string;
}

/**
 * Line items card (BR-09 to BR-12): regular and optional sections, each ordered independently.
 * Presentational: the page owns the lines and the amounts. Reordering by keyboard uses the Move
 * up / Move down actions of each line menu; the drag handle is a pointer enhancement that calls
 * the same move through the page.
 */
@Component({
  selector: 'app-quote-line-items',
  imports: [
    NgTemplateOutlet,
    FormsModule,
    ButtonDirective,
    InputText,
    Menu,
    Select,
    Textarea,
    CatalogPickerDialog,
  ],
  templateUrl: './quote-line-items.html',
  styleUrl: './quote-line-items.scss',
})
export class QuoteLineItems {
  readonly lines = input.required<readonly QuoteLine[]>();
  readonly errors = input.required<Readonly<Record<string, string>>>();
  readonly currency = input.required<string>();
  /** Amounts of the latest calculation by line `uid` (matched by request index). */
  readonly amounts = input.required<ReadonlyMap<string, CalculatedLine>>();
  readonly calcStatus = input.required<CalcStatus>();
  readonly disabled = input(false);

  readonly lineChange = output<LineChange>();
  readonly lineMove = output<LineMove>();
  readonly lineDrop = output<{ readonly from: number; readonly to: number }>();
  readonly lineRemove = output<string>();
  readonly lineAdd = output<LineAdd>();

  readonly emptyMessage = EMPTY_LINES_MESSAGE;
  readonly taxOptions: TaxOption[] = [
    { code: true, label: 'Taxable' },
    { code: false, label: 'Non-taxable' },
  ];

  readonly picker = signal<PickerTarget | null>(null);
  private readonly menu = viewChild.required(Menu);
  private readonly menuUid = signal<string | null>(null);
  /** Lines whose unit and cost row was opened by the menu. */
  private readonly opened = signal<ReadonlySet<string>>(new Set());
  readonly dragFrom = signal<number | null>(null);
  readonly dragOver = signal<string | null>(null);

  readonly regular = computed<Row[]>(() => this.rows(false));
  readonly optional = computed<Row[]>(() => this.rows(true));

  readonly menuModel = computed<MenuItem[]>(() => {
    const lines = this.lines();
    const index = lines.findIndex((line) => line.uid === this.menuUid());
    const uid = this.menuUid() ?? '';
    return [
      {
        label: 'Move up',
        disabled: !canMove(lines, index, -1),
        command: () => this.lineMove.emit({ uid, direction: -1 }),
      },
      {
        label: 'Move down',
        disabled: !canMove(lines, index, 1),
        command: () => this.lineMove.emit({ uid, direction: 1 }),
      },
      {
        label: this.opened().has(uid) ? 'Hide unit and cost' : 'Edit unit and cost',
        command: () => this.toggleDetails(uid),
      },
    ];
  });

  private rows(optional: boolean): Row[] {
    return this.lines()
      .map((line, index) => ({ line, index }))
      .filter((row) => row.line.isOptional === optional);
  }

  /** Open by the menu, or forced open while the unit or cost has an error. */
  detailsOpen(row: Row): boolean {
    return (
      this.opened().has(row.line.uid) ||
      this.error(row, 'unit') !== null ||
      this.error(row, 'unitCost') !== null
    );
  }

  private toggleDetails(uid: string): void {
    this.opened.update((set) => {
      const next = new Set(set);
      if (!next.delete(uid)) {
        next.add(uid);
      }
      return next;
    });
  }

  name(row: Row): string {
    return lineName(row.line, row.index);
  }

  /** "Item name, line 2": the position is part of every control's accessible name. */
  label(field: string, row: Row): string {
    return `${field}, line ${row.index + 1}`;
  }

  error(row: Row, field: string): string | null {
    return this.errors()[`${row.line.uid}.${field}`] ?? null;
  }

  describedBy(row: Row, field: string): string | null {
    return this.error(row, field) === null ? null : `line-${row.line.uid}-${field}-error`;
  }

  amount(row: Row): string {
    if (!isLineValid(row.line)) {
      return '—';
    }
    const calculated = this.amounts().get(row.line.uid);
    return calculated === undefined ? '—' : formatMoney(calculated.lineSubtotal, this.currency());
  }

  isBusy(): boolean {
    return this.calcStatus() === 'loading';
  }

  typeLabel(type: LineType): string {
    return type === 'product' ? 'Material' : 'Service';
  }

  patch(row: Row, patch: Partial<QuoteLine>): void {
    this.lineChange.emit({ uid: row.line.uid, patch });
  }

  openMenu(event: Event, row: Row): void {
    this.menuUid.set(row.line.uid);
    this.menu().toggle(event);
  }

  openPicker(type: LineType | null, optional: boolean): void {
    this.picker.set({ type, optional });
  }

  onPicked(picked: PickedLine): void {
    const target = this.picker();
    this.picker.set(null);
    if (target !== null) {
      this.lineAdd.emit({ ...picked, isOptional: target.optional });
    }
  }

  // Drag and drop (enhancement): the section check lives in `moveLine`.

  onDragStart(event: DragEvent, row: Row): void {
    this.dragFrom.set(row.index);
    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', row.line.uid);
    }
  }

  onDragOver(event: DragEvent, row: Row): void {
    const from = this.dragFrom();
    if (from !== null && this.lines()[from]?.isOptional === row.line.isOptional) {
      event.preventDefault();
      this.dragOver.set(row.line.uid);
    }
  }

  onDrop(event: DragEvent, row: Row): void {
    event.preventDefault();
    const from = this.dragFrom();
    this.onDragEnd();
    if (from !== null) {
      this.lineDrop.emit({ from, to: row.index });
    }
  }

  onDragEnd(): void {
    this.dragFrom.set(null);
    this.dragOver.set(null);
  }
}
