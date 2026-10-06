import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';

import { TaskRow, taskName } from '../../utils/work-order-form';

/**
 * Tasks & checklist card (BR-10). Presentational: the page owns the list. The drag handle is a
 * pointer enhancement; Reordering is by drag only (user-approved deviation from BR-10 keyboard buttons).
 */
@Component({
  selector: 'app-work-order-tasks',
  imports: [FormsModule, ButtonDirective, InputText],
  templateUrl: './work-order-tasks.html',
  styleUrl: './work-order-tasks.scss',
})
export class WorkOrderTasks {
  readonly tasks = input.required<readonly TaskRow[]>();
  readonly errors = input.required<Readonly<Record<string, string>>>();
  readonly disabled = input(false);

  readonly labelChange = output<{ readonly uid: string; readonly label: string }>();
  readonly dropped = output<{ readonly from: number; readonly to: number }>();
  readonly removed = output<string>();
  readonly added = output<void>();
  readonly templateRequested = output<void>();

  readonly dragFrom = signal<number | null>(null);
  readonly dragOver = signal<string | null>(null);

  name(task: TaskRow, index: number): string {
    return taskName(task, index);
  }

  error(task: TaskRow): string | null {
    return this.errors()[`tasks[${task.uid}]`] ?? null;
  }

  onDragStart(event: DragEvent, index: number, task: TaskRow): void {
    this.dragFrom.set(index);
    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', task.uid);
    }
  }

  onDragOver(event: DragEvent, task: TaskRow): void {
    if (this.dragFrom() !== null) {
      event.preventDefault();
      this.dragOver.set(task.uid);
    }
  }

  onDrop(event: DragEvent, index: number): void {
    event.preventDefault();
    const from = this.dragFrom();
    this.onDragEnd();
    if (from !== null) {
      this.dropped.emit({ from, to: index });
    }
  }

  onDragEnd(): void {
    this.dragFrom.set(null);
    this.dragOver.set(null);
  }
}
