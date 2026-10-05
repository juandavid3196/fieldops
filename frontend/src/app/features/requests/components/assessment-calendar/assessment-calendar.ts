import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { AssessmentCalendar } from '../../models/requests.model';
import {
  CalendarView,
  CalendarViewModel,
  ProposedSlot,
  buildCalendarView,
  rangeLabel,
} from '../../utils/assessment-calendar';

export const CALENDAR_ERROR_MESSAGE = "We couldn't load the calendar.";
export const CALENDAR_NO_TECHNICIAN_MESSAGE = 'Select a technician to see their schedule.';
export const CALENDAR_EMPTY_AGENDA_MESSAGE = 'No commitments this week.';

export const CALENDAR_VIEWS: readonly { readonly code: CalendarView; readonly label: string }[] = [
  { code: 'week', label: 'Week' },
  { code: 'day', label: 'Day' },
  { code: 'agenda', label: 'Agenda' },
];

/**
 * Read-only calendar of the selected technician (BR-07): Week, Day and Agenda views plus the
 * proposed block. Nothing is selectable; the event list below is the accessible equivalent.
 */
@Component({
  selector: 'app-assessment-calendar',
  imports: [ButtonDirective, Message, Skeleton],
  templateUrl: './assessment-calendar.html',
  styleUrl: './assessment-calendar.scss',
})
export class AssessmentCalendarView {
  readonly calendar = input<AssessmentCalendar | null>(null);
  readonly hasTechnician = input(false);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly view = input<CalendarView>('week');
  /** The date the view is anchored on (the form date by default). */
  readonly viewDate = input.required<string>();
  readonly proposed = input<ProposedSlot | null>(null);
  readonly timezone = input('UTC');

  readonly viewChange = output<CalendarView>();
  readonly shift = output<-1 | 1>();
  readonly retry = output<void>();

  readonly views = CALENDAR_VIEWS;
  readonly errorMessage = CALENDAR_ERROR_MESSAGE;
  readonly noTechnicianMessage = CALENDAR_NO_TECHNICIAN_MESSAGE;
  readonly emptyAgendaMessage = CALENDAR_EMPTY_AGENDA_MESSAGE;

  readonly model = computed<CalendarViewModel | null>(() => {
    const calendar = this.calendar();
    return calendar === null
      ? null
      : buildCalendarView({
          calendar,
          view: this.view(),
          viewDate: this.viewDate(),
          proposed: this.proposed(),
          timeZone: this.timezone(),
        });
  });

  readonly label = computed(() =>
    rangeLabel(
      this.view(),
      this.viewDate(),
      this.view() === 'week' ? this.model()?.columns.map((column) => column.date) : undefined,
    ),
  );
  readonly step = computed(() => (this.view() === 'day' ? 'day' : 'week'));
}
