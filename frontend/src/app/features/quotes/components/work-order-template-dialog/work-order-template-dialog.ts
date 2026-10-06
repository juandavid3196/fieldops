import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import {
  ChecklistTemplate,
  MAX_TASKS,
  TASK_LIMIT_MESSAGE,
} from '../../../jobs/models/work-order.model';
import { WorkOrdersService } from '../../../jobs/services/work-orders.service';
import { sortTemplates } from '../../utils/work-order-form';

export const TEMPLATES_ERROR_MESSAGE = "We couldn't load templates.";
export const NO_TEMPLATES_MESSAGE = 'No checklist templates yet.';
export const TEMPLATE_SAVE_FAILED_MESSAGE = "We couldn't save the template. Try again.";

type ListState = 'loading' | 'ready' | 'error';

/**
 * Checklist template dialog (BR-11): lists the organization's active templates (the selected
 * category first), appends the chosen one to the tasks, and saves the current tasks as a new
 * template. Applying never keeps a link to the template.
 */
@Component({
  selector: 'app-work-order-template-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Message, Skeleton],
  templateUrl: './work-order-template-dialog.html',
  styleUrl: './work-order-template-dialog.scss',
})
export class WorkOrderTemplateDialog {
  private readonly service = inject(WorkOrdersService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly categoryId = input.required<string | null>();
  /** Number of tasks already in the form (the 50-task limit counts them). */
  readonly taskCount = input.required<number>();
  /** Current non-empty task labels, trimmed. */
  readonly labels = input.required<readonly string[]>();

  readonly applied = output<readonly string[]>();
  readonly templateSaved = output<void>();
  readonly dismissed = output<void>();

  readonly state = signal<ListState>('loading');
  private readonly loaded = signal<readonly ChecklistTemplate[]>([]);
  readonly templates = computed(() => sortTemplates(this.loaded(), this.categoryId()));
  readonly selectedId = signal<string | null>(null);
  readonly limitError = signal<string | null>(null);
  readonly name = signal('');
  readonly errors = signal<Readonly<Record<string, string>>>({});
  readonly saving = signal(false);

  readonly errorMessage = TEMPLATES_ERROR_MESSAGE;
  readonly emptyMessage = NO_TEMPLATES_MESSAGE;

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.selectedId.set(null);
          this.limitError.set(null);
          this.name.set('');
          this.errors.set({});
          this.load();
        });
      }
    });
  }

  load(): void {
    this.state.set('loading');
    this.service
      .templates()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (templates) => {
          this.loaded.set(templates);
          this.state.set('ready');
        },
        error: () => this.state.set('error'),
      });
  }

  taskCountLabel(template: ChecklistTemplate): string {
    const count = template.items.length;
    return `${count} ${count === 1 ? 'task' : 'tasks'}`;
  }

  select(id: string): void {
    this.selectedId.set(id);
    this.limitError.set(null);
  }

  apply(): void {
    const template = this.loaded().find((candidate) => candidate.id === this.selectedId());
    if (template === undefined) {
      return;
    }
    if (this.taskCount() + template.items.length > MAX_TASKS) {
      this.limitError.set(TASK_LIMIT_MESSAGE);
      return;
    }
    this.applied.emit(template.items.map((item) => item.label));
  }

  onName(value: string): void {
    this.name.set(value);
    this.errors.set({});
  }

  saveCurrent(): void {
    if (this.saving()) {
      return;
    }
    const name = this.name().trim();
    const items = this.labels();
    const errors: Record<string, string> = {};
    if (name.length === 0 || name.length > 120) {
      errors['name'] = 'Enter a template name of 120 characters or fewer.';
    }
    if (items.length === 0 || items.length > MAX_TASKS) {
      errors['items'] = 'Add between 1 and 50 tasks to save a template.';
    }
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    this.saving.set(true);
    this.service
      .createTemplate({
        name,
        serviceCategoryId: this.categoryId(),
        items: items.map((label) => ({ label })),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (template) => {
          this.saving.set(false);
          this.loaded.update((list) => [...list, template]);
          this.name.set('');
          this.templateSaved.emit();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const fieldErrors = isApiError(error) ? error.fieldErrors : {};
          const mapped: Record<string, string> = {};
          for (const key of ['name', 'items', 'serviceCategoryId']) {
            const message = Object.entries(fieldErrors).find(
              ([path]) => path.replace(/\[\d+\].*$/, '').toLowerCase() === key.toLowerCase(),
            )?.[1][0];
            if (message !== undefined) {
              mapped[key] = message;
            }
          }
          this.errors.set(
            Object.keys(mapped).length > 0 ? mapped : { name: TEMPLATE_SAVE_FAILED_MESSAGE },
          );
        },
      });
  }
}
