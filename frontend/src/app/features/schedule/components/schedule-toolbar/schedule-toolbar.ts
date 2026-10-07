import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';

import {
  NamedOption,
  ScheduleBranch,
  ScheduleView,
  VisitStatusFilter,
} from '../../models/schedule.model';

export const MAP_UNAVAILABLE_MESSAGE = "Map view isn't available yet.";

const STATUS_CHOICES: { code: VisitStatusFilter; label: string }[] = [
  { code: 'all', label: 'All' },
  { code: 'unassigned', label: 'Unassigned' },
  { code: 'assigned', label: 'Assigned' },
  { code: 'conflicts', label: 'With conflicts' },
];

/** Header, navigation, Day/Week toggle and the Branch/Team/Skills/Status filters (BR-20). */
@Component({
  selector: 'app-schedule-toolbar',
  imports: [FormsModule, ButtonDirective, MultiSelect, Select],
  templateUrl: './schedule-toolbar.html',
  styleUrl: './schedule-toolbar.scss',
})
export class ScheduleToolbar {
  readonly view = input.required<ScheduleView>();
  readonly rangeLabel = input.required<string>();
  readonly branches = input<readonly ScheduleBranch[]>([]);
  readonly branchId = input<string | null>(null);
  readonly teamOptions = input<readonly NamedOption[]>([]);
  readonly teamIds = input<readonly string[]>([]);
  readonly skills = input<readonly NamedOption[]>([]);
  readonly skillId = input<string | null>(null);
  readonly status = input<VisitStatusFilter>('all');
  /** Below 768px the filters sit in a collapsible section. */
  readonly compact = input(false);

  readonly todayRequested = output<void>();
  readonly previousRequested = output<void>();
  readonly nextRequested = output<void>();
  readonly viewChange = output<ScheduleView>();
  readonly branchChange = output<string>();
  readonly teamChange = output<readonly string[]>();
  readonly skillChange = output<string | null>();
  readonly statusChange = output<VisitStatusFilter>();

  readonly mapMessage = MAP_UNAVAILABLE_MESSAGE;
  readonly statusChoices = STATUS_CHOICES;
  readonly filtersOpen = signal(false);
  readonly branchOptions = computed(() => [...this.branches()]);
  readonly teamChoices = computed(() => [...this.teamOptions()]);
  readonly skillChoices = computed(() => [...this.skills()]);

  /** Local select value so a cancelled change (discard dialog) can snap back to the URL value. */
  readonly branchModel = linkedSignal(() => this.branchId());

  teamNames(ids: readonly string[] | null): string {
    const names = this.teamChoices()
      .filter((option) => ids?.includes(option.id))
      .map((option) => option.name);
    return names.length > 0 ? names.join(', ') : 'All';
  }

  resetBranch(): void {
    this.branchModel.set(this.branchId());
  }

  onBranch(id: string): void {
    this.branchModel.set(id);
    this.branchChange.emit(id);
  }
}
