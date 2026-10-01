import {
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Menu } from 'primeng/menu';
import { Tag } from 'primeng/tag';
import { Observable } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { TechnicianException } from '../../models/skills-availability.model';
import { SkillsAvailabilityService } from '../../services/skills-availability.service';
import {
  KIND_LABELS,
  exceptionTime,
  formatExceptionDate,
  todayIn,
} from '../../utils/skills-availability';
import { ExceptionDialog, exceptionConflictMessage } from '../exception-dialog/exception-dialog';

export const NO_EXCEPTIONS_MESSAGE = 'No upcoming exceptions.';

/** BR-16 exceptions card: list, Add/Edit dialog and Edit / Cancel / Activate row actions. */
@Component({
  selector: 'app-availability-exceptions',
  imports: [ButtonDirective, ExceptionDialog, Menu, Tag],
  templateUrl: './availability-exceptions.html',
  styleUrl: './availability-exceptions.scss',
})
export class AvailabilityExceptions {
  private readonly service = inject(SkillsAvailabilityService);
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly menu = viewChild.required(Menu);

  readonly technicianId = input.required<string>();
  readonly exceptions = input.required<readonly TechnicianException[]>();
  readonly timezone = input('');
  readonly readOnly = input(false);

  readonly changed = output<void>();
  readonly unauthorized = output<void>();
  readonly unavailable = output<void>();
  /** The dialog, for the page's leave guard. */
  readonly dialog = viewChild(ExceptionDialog);

  protected readonly emptyMessage = NO_EXCEPTIONS_MESSAGE;
  protected readonly dialogOpen = signal(false);
  protected readonly editTarget = signal<TechnicianException | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly menuModel = signal<MenuItem[]>([]);
  protected readonly today = computed(() => todayIn(this.timezone()));
  protected readonly rows = computed(() =>
    this.exceptions().map((item) => ({
      item,
      date: formatExceptionDate(item.date),
      kind: KIND_LABELS[item.kind],
      time: item.kind === 'unavailable' ? 'All day' : exceptionTime(item.start, item.end),
      active: item.status === 'active',
    })),
  );

  protected openAdd(): void {
    this.editTarget.set(null);
    this.dialogOpen.set(true);
  }

  protected openMenu(event: Event, item: TechnicianException): void {
    const items: MenuItem[] =
      item.status === 'active'
        ? [
            {
              label: 'Edit',
              icon: 'pi pi-pencil',
              command: () => {
                this.editTarget.set(item);
                this.dialogOpen.set(true);
              },
            },
            { label: 'Cancel', icon: 'pi pi-ban', command: () => this.confirmCancel(item) },
          ]
        : [
            {
              label: 'Activate',
              icon: 'pi pi-replay',
              command: () =>
                this.run(
                  item,
                  this.service.activateException(this.technicianId(), item.id, item.version),
                  'Exception activated.',
                ),
            },
          ];
    this.menuModel.set(items);
    this.menu().toggle(event);
  }

  private confirmCancel(item: TechnicianException): void {
    this.confirmation.confirm({
      header: 'Cancel this exception?',
      message: 'The weekly schedule applies again on that date. You can activate it later.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Cancel exception' },
      rejectButtonProps: { label: 'Keep exception', severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          item,
          this.service.cancelException(this.technicianId(), item.id, item.version),
          'Exception cancelled.',
        ),
    });
  }

  private run(
    item: TechnicianException,
    request: Observable<TechnicianException>,
    success: string,
  ): void {
    if (this.busyId() !== null) {
      return;
    }
    this.busyId.set(item.id);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busyId.set(null);
        this.messages.add({ severity: 'success', summary: success });
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        if (!isApiError(error)) {
          this.messages.add({
            severity: 'error',
            summary: "We couldn't update the exception. Try again.",
          });
        } else if (error.kind === 'unauthorized') {
          this.unauthorized.emit();
        } else if (error.kind === 'not-found') {
          this.unavailable.emit();
        } else {
          this.messages.add({
            severity: 'error',
            summary:
              error.kind === 'conflict'
                ? exceptionConflictMessage(error, false)
                : "We couldn't update the exception. Try again.",
          });
          if (error.kind === 'conflict') {
            this.changed.emit();
          }
        }
      },
    });
  }
}
