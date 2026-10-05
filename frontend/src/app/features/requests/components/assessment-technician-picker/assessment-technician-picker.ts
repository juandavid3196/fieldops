import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { PlannerTechnician, SlotState } from '../../models/requests.model';
import { slotChipText, workloadText } from '../../utils/assessment-schedule';

export const PLANNER_ERROR_MESSAGE = "We couldn't load technician availability.";
export const NO_TECHNICIANS_MESSAGE = 'No active technicians in this branch.';
export const CHOOSE_BRANCH_MESSAGE = 'Choose a branch to see technicians.';

const CHIP_ICONS: Readonly<Record<SlotState, string>> = {
  available: 'pi-check-circle',
  available_after: 'pi-clock',
  outside_availability: 'pi-info-circle',
  conflict: 'pi-exclamation-triangle',
  time_off: 'pi-ban',
};

interface Card {
  readonly technician: PlannerTechnician;
  readonly chip: string;
  readonly icon: string;
  readonly workload: string;
  readonly name: string;
}

/**
 * "Select a technician" cards (BR-04 to BR-06): a radio group with a roving tabindex. State is
 * conveyed by chip text and icon, never by color alone. Presentational: the page owns the data.
 */
@Component({
  selector: 'app-assessment-technician-picker',
  imports: [RouterLink, ButtonDirective, Message, Skeleton],
  templateUrl: './assessment-technician-picker.html',
  styleUrl: './assessment-technician-picker.scss',
})
export class AssessmentTechnicianPicker {
  /** `null` until the planner answers. */
  readonly technicians = input<readonly PlannerTechnician[] | null>(null);
  readonly timezone = input('UTC');
  readonly selectedId = input('');
  /** The slot changed: cards keep their content with a loading indicator. */
  readonly loading = input(false);
  readonly failed = input(false);
  /** A request without a branch waits for the Branch select. */
  readonly waitingForBranch = input(false);

  readonly selectedChange = output<string>();
  readonly retry = output<void>();

  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly errorMessage = PLANNER_ERROR_MESSAGE;
  readonly emptyMessage = NO_TECHNICIANS_MESSAGE;
  readonly chooseBranchMessage = CHOOSE_BRANCH_MESSAGE;

  readonly cards = computed<Card[]>(() =>
    (this.technicians() ?? []).map((technician) => {
      const chip = slotChipText(technician, this.timezone());
      const workload = workloadText(technician);
      return {
        technician,
        chip,
        icon: CHIP_ICONS[technician.slot.state],
        workload,
        name: `${technician.name}, ${chip}, workload ${workload}`,
      };
    }),
  );

  /** Roving tabindex: the selected card, else the first one, is the tab stop. */
  readonly tabStopId = computed(() => {
    const cards = this.cards();
    return cards.some((card) => card.technician.id === this.selectedId())
      ? this.selectedId()
      : (cards[0]?.technician.id ?? '');
  });

  select(id: string): void {
    this.selectedChange.emit(id);
  }

  onKeydown(event: KeyboardEvent, index: number): void {
    const cards = this.cards();
    let target: number;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        target = (index + 1) % cards.length;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        target = (index - 1 + cards.length) % cards.length;
        break;
      case 'Home':
        target = 0;
        break;
      case 'End':
        target = cards.length - 1;
        break;
      case ' ':
      case 'Enter':
        event.preventDefault();
        this.select(cards[index].technician.id);
        return;
      default:
        return;
    }
    event.preventDefault();
    this.select(cards[target].technician.id);
    this.host.querySelectorAll<HTMLElement>('[role="radio"]')[target]?.focus();
  }
}
