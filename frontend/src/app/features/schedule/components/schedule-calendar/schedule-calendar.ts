import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { visitStatusLabel } from '../../../jobs/utils/work-order-format';
import {
  CalendarRange,
  CalendarResponse,
  CalendarTechnician,
  CalendarVisit,
  ScheduleView,
} from '../../models/schedule.model';
import { LoadStatus, ProposedBlock } from '../../services/schedule-page.store';
import {
  HourAxis,
  LoadInfo,
  blockGeometry,
  formatBlockRange,
  formatMinutes,
  hourAxis,
  loadInfo,
  monthDay,
  numberText,
  proposedMinutes,
  visibleDays,
  weekdayShort,
  zoned,
} from '../../utils/schedule-range';

export const LOAD_ERROR_MESSAGE = "We couldn't load the schedule. Try again.";
export const NO_TECHNICIANS_MESSAGE = 'No active technicians in this branch.';

export interface BlockVm {
  readonly key: string;
  readonly kind: 'visit' | 'assessment' | 'proposed';
  readonly visitId: string | null;
  readonly time: string;
  readonly title: string;
  readonly number: string;
  readonly street: string;
  readonly conflict: boolean;
  readonly selected: boolean;
  readonly ariaLabel: string;
  readonly left: number;
  readonly width: number;
  readonly sortKey: number;
}

export interface SegmentVm {
  readonly left: number;
  readonly width: number;
}

export interface CellVm {
  readonly date: string;
  readonly off: boolean;
  readonly timeOff: boolean;
  readonly blocks: readonly BlockVm[];
  readonly available: readonly SegmentVm[];
  readonly breaks: readonly SegmentVm[];
  readonly timeOffSegments: readonly SegmentVm[];
}

export interface LaneVm {
  readonly id: string;
  readonly unassigned: boolean;
  readonly technician: CalendarTechnician | null;
  readonly load: LoadInfo | null;
  readonly cells: readonly CellVm[];
}

export interface DayHeaderVm {
  readonly date: string;
  readonly weekday: string;
  readonly monthDay: string;
  readonly today: boolean;
}

/** Presentational Day/Week calendar: lanes per technician, blocks, load, legend (BR-04, BR-05). */
@Component({
  selector: 'app-schedule-calendar',
  imports: [NgTemplateOutlet, RouterLink, ButtonDirective, Message, Skeleton],
  templateUrl: './schedule-calendar.html',
  styleUrl: './schedule-calendar.scss',
})
export class ScheduleCalendar {
  readonly calendar = input<CalendarResponse | null>(null);
  readonly status = input<LoadStatus>('loading');
  readonly view = input<ScheduleView>('week');
  readonly timezone = input('UTC');
  readonly proposed = input<ProposedBlock | null>(null);
  readonly selectedVisitId = input<string | null>(null);
  readonly today = input('');
  /** Current local minutes since midnight, for the Day view line. */
  readonly nowMinutes = input(0);

  readonly visitSelected = output<string>();
  readonly retry = output<void>();

  readonly errorMessage = LOAD_ERROR_MESSAGE;
  readonly noTechniciansMessage = NO_TECHNICIANS_MESSAGE;
  readonly skeletons = [0, 1, 2];

  readonly days = computed<readonly string[]>(() => {
    const calendar = this.calendar();
    return calendar === null
      ? []
      : visibleDays(calendar.days, calendar.technicians, calendar.visits, this.timezone());
  });
  readonly headers = computed<readonly DayHeaderVm[]>(() =>
    this.days().map((date) => ({
      date,
      weekday: weekdayShort(date),
      monthDay: monthDay(date),
      today: date === this.today(),
    })),
  );
  readonly axis = computed<HourAxis>(() =>
    hourAxis(this.calendar()?.technicians ?? [], this.days()[0] ?? '', this.timezone()),
  );
  readonly axisLabels = computed(() =>
    this.axis().hours.map((hour) => ({
      hour,
      label: formatMinutes(hour * 60),
      left: ((hour - this.axis().startHour) / (this.axis().endHour - this.axis().startHour)) * 100,
    })),
  );
  readonly showNow = computed(
    () =>
      this.view() === 'day' &&
      this.days()[0] === this.today() &&
      this.nowMinutes() >= this.axis().startHour * 60 &&
      this.nowMinutes() <= this.axis().endHour * 60,
  );
  readonly nowLeft = computed(
    () => blockGeometry(this.nowMinutes(), this.nowMinutes(), this.axis()).left,
  );
  readonly hasTechnicians = computed(() => (this.calendar()?.technicians.length ?? 0) > 0);

  readonly lanes = computed<readonly LaneVm[]>(() => {
    const calendar = this.calendar();
    if (calendar === null) {
      return [];
    }
    const proposed = proposedMinutes(this.proposed() ?? { date: null, start: null, end: null });
    const proposedIds = this.proposed()?.technicianIds ?? [];
    const unassigned: LaneVm = this.buildLane(calendar, null, proposed, proposedIds);
    return [
      unassigned,
      ...calendar.technicians.map((technician) =>
        this.buildLane(calendar, technician, proposed, proposedIds),
      ),
    ];
  });

  private buildLane(
    calendar: CalendarResponse,
    technician: CalendarTechnician | null,
    proposed: ReturnType<typeof proposedMinutes>,
    proposedIds: readonly string[],
  ): LaneVm {
    const tz = this.timezone();
    const axis = this.axis();
    const selected = this.selectedVisitId();
    const visits = calendar.visits.filter((visit) =>
      technician === null
        ? visit.technicianIds.length === 0 && visit.status === 'scheduled'
        : visit.technicianIds.includes(technician.id),
    );
    const segment = (range: CalendarRange): SegmentVm => {
      const geometry = blockGeometry(
        zoned(range.start, tz).minutes,
        zoned(range.end, tz).minutes,
        axis,
      );
      return { left: geometry.left, width: geometry.width };
    };
    const cells = this.days().map((date): CellVm => {
      const entry = technician?.days.find((day) => day.date === date);
      const blocks: BlockVm[] = visits
        .filter((visit) => zoned(visit.start, tz).date === date)
        .map((visit) => this.visitBlock(visit, selected, axis, tz));
      for (const range of technician?.assessments ?? []) {
        if (zoned(range.start, tz).date === date) {
          blocks.push(this.assessmentBlock(range, axis, tz));
        }
      }
      const showProposed =
        proposed !== null &&
        proposed.date === date &&
        (technician === null ? proposedIds.length === 0 : proposedIds.includes(technician.id));
      if (showProposed) {
        const geometry = blockGeometry(proposed.startMinutes, proposed.endMinutes, axis);
        const time = `${formatMinutes(proposed.startMinutes)} – ${formatMinutes(proposed.endMinutes)}`;
        blocks.push({
          key: `proposed-${date}`,
          kind: 'proposed',
          visitId: null,
          time,
          title: 'Proposed',
          number: '',
          street: '',
          conflict: false,
          selected: false,
          ariaLabel: `Proposed schedule ${time}`,
          left: geometry.left,
          width: geometry.width,
          sortKey: proposed.startMinutes,
        });
      }
      blocks.sort((a, b) => a.sortKey - b.sortKey);
      return {
        date,
        off: technician !== null && (entry?.availability.length ?? 0) === 0,
        timeOff: (entry?.timeOff.length ?? 0) > 0,
        blocks,
        available: (entry?.availability ?? []).map(segment),
        breaks: (entry?.breaks ?? []).map(segment),
        timeOffSegments: (entry?.timeOff ?? []).map(segment),
      };
    });
    return {
      id: technician?.id ?? 'unassigned',
      unassigned: technician === null,
      technician,
      load: technician === null ? null : loadInfo(technician.load),
      cells,
    };
  }

  private visitBlock(
    visit: CalendarVisit,
    selected: string | null,
    axis: HourAxis,
    tz: string,
  ): BlockVm {
    const start = zoned(visit.start, tz).minutes;
    const end = zoned(visit.end, tz).minutes;
    const geometry = blockGeometry(start, end, axis);
    const conflict = visit.conflicts.length > 0;
    const time = formatBlockRange(visit.start, visit.end, tz);
    const number = numberText(visit.displayNumber);
    return {
      key: visit.visitId,
      kind: 'visit',
      visitId: visit.visitId,
      time,
      title: visit.title,
      number,
      street: visit.street,
      conflict,
      selected: visit.visitId === selected,
      ariaLabel: `${time}, ${visit.title}, ${number}, ${visitStatusLabel(visit.status)}${conflict ? ', conflict' : ''}`,
      left: geometry.left,
      width: geometry.width,
      sortKey: start,
    };
  }

  private assessmentBlock(range: CalendarRange, axis: HourAxis, tz: string): BlockVm {
    const start = zoned(range.start, tz).minutes;
    const geometry = blockGeometry(start, zoned(range.end, tz).minutes, axis);
    const time = formatBlockRange(range.start, range.end, tz);
    return {
      key: `assessment-${range.start}`,
      kind: 'assessment',
      visitId: null,
      time,
      title: 'Assessment',
      number: '',
      street: '',
      conflict: false,
      selected: false,
      ariaLabel: `Assessment ${time}`,
      left: geometry.left,
      width: geometry.width,
      sortKey: start,
    };
  }
}
