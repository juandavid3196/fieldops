import { Component, computed, inject, input, output, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Observable } from 'rxjs';

import { TechnicianVisitDetail, VisitTask } from '../../models/technician-visits.model';
import {
  classifyMutationFailure,
  completionPercent,
  remainingRequired,
  requiredText,
} from '../../utils/job-progress';
import { formatTime } from '../../utils/technician-format';
import { scopeLines } from '../../utils/visit-detail';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { JobScope } from '../job-scope/job-scope';

export const COMMENT_INVALID_MESSAGE = 'Enter a comment up to 1000 characters.';
export const LABEL_INVALID_MESSAGE = 'Enter a task name up to 240 characters.';

/** BR-16 Tasks section: completion card, checklist, comments, optional tasks and original scope. */
@Component({
  selector: 'app-job-tasks',
  imports: [ButtonDirective, JobScope],
  templateUrl: './job-tasks.html',
  styleUrl: './job-tasks.scss',
})
export class JobTasks {
  private readonly visits = inject(TechnicianVisitsService);
  private scopeTrigger: HTMLElement | null = null;

  readonly visit = input.required<TechnicianVisitDetail>();
  /** Non-primary technicians read only. */
  readonly readOnly = input(false);
  readonly visitChange = output<TechnicianVisitDetail>();
  readonly reload = output<void>();

  /** Request in flight; blocks repeats. */
  readonly pending = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly commentTaskId = signal<string | null>(null);
  readonly commentDraft = signal('');
  readonly addOpen = signal(false);
  readonly labelDraft = signal('');
  readonly scopeOpen = signal(false);

  readonly tasks = computed(() => this.visit().tasks);
  readonly completed = computed(() => this.tasks().filter((task) => task.isCompleted).length);
  readonly percent = computed(() => completionPercent(this.tasks()));
  readonly remaining = computed(() => remainingRequired(this.tasks()));
  readonly requiredText = computed(() => requiredText(this.remaining()));
  readonly scope = computed(() => scopeLines(this.visit().scope));

  completedTime(task: VisitTask): string {
    return task.completedAt === null ? '' : formatTime(task.completedAt, this.visit().timezone);
  }

  toggle(task: VisitTask, box: HTMLInputElement): void {
    this.run(
      `toggle:${task.id}`,
      this.visits.updateTask(this.visit().visitId, task.id, { isCompleted: box.checked }),
      undefined,
      () => (box.checked = task.isCompleted),
    );
  }

  openComment(task: VisitTask): void {
    this.error.set(null);
    this.commentTaskId.set(task.id);
    this.commentDraft.set(task.notes ?? '');
  }

  saveComment(task: VisitTask): void {
    const notes = this.commentDraft().trim();
    if (notes === (task.notes ?? '')) {
      this.commentTaskId.set(null);
      return;
    }
    this.run(
      `comment:${task.id}`,
      this.visits.updateTask(this.visit().visitId, task.id, { notes }),
      COMMENT_INVALID_MESSAGE,
      undefined,
      () => this.commentTaskId.set(null),
    );
  }

  cancelComment(): void {
    this.error.set(null);
    this.commentTaskId.set(null);
  }

  openAdd(): void {
    this.error.set(null);
    this.labelDraft.set('');
    this.addOpen.set(true);
  }

  add(): void {
    const label = this.labelDraft().trim();
    if (label === '') {
      return;
    }
    this.run(
      'add',
      this.visits.addTask(this.visit().visitId, label),
      LABEL_INVALID_MESSAGE,
      undefined,
      () => this.addOpen.set(false),
    );
  }

  cancelAdd(): void {
    this.error.set(null);
    this.addOpen.set(false);
  }

  openScope(trigger: HTMLElement): void {
    this.scopeTrigger = trigger;
    this.scopeOpen.set(true);
  }

  closeScope(): void {
    if (this.scopeOpen()) {
      this.scopeOpen.set(false);
      const target = this.scopeTrigger;
      setTimeout(() => target?.focus());
    }
  }

  /** One request at a time; success replaces the visit, failure keeps the local input. */
  private run(
    key: string,
    request: Observable<TechnicianVisitDetail>,
    invalid?: string,
    onFailure?: () => void,
    onSuccess?: () => void,
  ): void {
    if (this.pending() !== null) {
      return;
    }
    this.pending.set(key);
    this.error.set(null);
    request.subscribe({
      next: (visit) => {
        this.pending.set(null);
        onSuccess?.();
        this.visitChange.emit(visit);
      },
      error: (error: unknown) => {
        const failure = classifyMutationFailure(error, { invalid });
        this.pending.set(null);
        this.error.set(failure.message);
        onFailure?.();
        if (failure.reload) {
          this.reload.emit();
        }
      },
    });
  }
}
