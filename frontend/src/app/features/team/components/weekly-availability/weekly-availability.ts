import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { DayForm } from '../../models/skills-availability.model';
import {
  DAY_NAMES,
  DayErrors,
  TIME_OPTIONS,
  breakOptions,
  breakValue,
  dayHours,
  formatHours,
  weeklyTotalHours,
  windowValid,
  withWindow,
} from '../../utils/skills-availability';
import { formatWindowTime } from '../../utils/team-format';

export const TIME_ORDER_MESSAGE = 'End time must be after start time.';

interface DayView {
  readonly day: DayForm;
  readonly name: string;
  readonly capacity: string;
  readonly breakOptions: ReturnType<typeof breakOptions>;
  readonly breakValue: string;
  readonly breakText: string;
  readonly startError: string | null;
  readonly endError: string | null;
  readonly breakError: string | null;
}

/** BR-05–BR-08 weekly table: presentational; the page owns the form state, copy and reset. */
@Component({
  selector: 'app-weekly-availability',
  imports: [FormsModule, ButtonDirective, Select, ToggleSwitch],
  templateUrl: './weekly-availability.html',
  styleUrl: './weekly-availability.scss',
})
export class WeeklyAvailability {
  readonly days = input.required<readonly DayForm[]>();
  readonly timezoneLabel = input('');
  readonly readOnly = input(false);
  readonly disabled = input(false);
  readonly errors = input<DayErrors>({});

  readonly daysChange = output<DayForm[]>();
  readonly copyRequested = output<void>();
  readonly resetRequested = output<void>();

  protected readonly timeOptions = TIME_OPTIONS;
  protected readonly views = computed<DayView[]>(() =>
    this.days().map((day) => {
      const errors = this.errors()[day.dayOfWeek] ?? {};
      return {
        day,
        name: DAY_NAMES[day.dayOfWeek],
        capacity: formatHours(dayHours(day)),
        breakOptions: breakOptions(day),
        breakValue: breakValue(day),
        breakText:
          day.breakStart !== null && day.breakEnd !== null
            ? `${formatWindowTime(day.breakStart)} – ${formatWindowTime(day.breakEnd)}`
            : 'No break',
        startError: errors['start'] ?? null,
        endError: windowValid(day) ? (errors['end'] ?? null) : TIME_ORDER_MESSAGE,
        breakError: errors['break'] ?? errors['breakstart'] ?? errors['breakend'] ?? null,
      };
    }),
  );
  protected readonly total = computed(() => weeklyTotalHours(this.days()));
  protected readonly formatTime = formatWindowTime;

  private update(dayOfWeek: number, change: (day: DayForm) => DayForm): void {
    this.daysChange.emit(
      this.days().map((day) => (day.dayOfWeek === dayOfWeek ? change(day) : day)),
    );
  }

  protected toggle(dayOfWeek: number, on: boolean): void {
    this.update(dayOfWeek, (day) => ({ ...day, on, breakStart: null, breakEnd: null }));
  }

  protected setStart(dayOfWeek: number, start: string): void {
    this.update(dayOfWeek, (day) => withWindow(day, { start }));
  }

  protected setEnd(dayOfWeek: number, end: string): void {
    this.update(dayOfWeek, (day) => withWindow(day, { end }));
  }

  protected setBreak(dayOfWeek: number, value: string): void {
    const [breakStart, breakEnd] = value === '' ? [null, null] : value.split('|');
    this.update(dayOfWeek, (day) => ({ ...day, breakStart, breakEnd }));
  }
}
