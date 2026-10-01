import { Component, computed, input, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Select } from 'primeng/select';
import { Tag } from 'primeng/tag';

import { CatalogSkill, SkillRow } from '../../models/skills-availability.model';
import {
  LEVEL_OPTIONS,
  addSkill,
  pickerSkills,
  removeSkill,
  setLevel,
  setPrimary,
  sortSkills,
} from '../../utils/skills-availability';

export const NO_SKILLS_MESSAGE = 'No skills assigned yet.';

/** BR-09/BR-10 skills card: presentational; the page owns the rows and the catalog. */
@Component({
  selector: 'app-operational-skills',
  imports: [FormsModule, ButtonDirective, Select, Tag],
  templateUrl: './operational-skills.html',
  styleUrl: './operational-skills.scss',
})
export class OperationalSkills {
  private readonly picker = viewChild(Select);

  readonly rows = input.required<readonly SkillRow[]>();
  readonly catalog = input<readonly CatalogSkill[]>([]);
  readonly readOnly = input(false);
  readonly disabled = input(false);
  readonly error = input<string | null>(null);

  readonly rowsChange = output<SkillRow[]>();
  readonly manageRequested = output<void>();

  protected readonly emptyMessage = NO_SKILLS_MESSAGE;
  protected readonly levels = LEVEL_OPTIONS;
  protected readonly sorted = computed(() => sortSkills(this.rows()));
  protected readonly choices = computed(() => pickerSkills(this.catalog(), this.rows()));

  protected levelName(level: number): string {
    return this.levels.find((option) => option.value === level)?.label ?? '';
  }

  protected add(skillId: string | null): void {
    const skill = this.choices().find((choice) => choice.id === skillId);
    if (skill) {
      this.rowsChange.emit(addSkill(this.rows(), skill));
    }
    this.picker()?.clear();
  }

  protected remove(skillId: string): void {
    this.rowsChange.emit(removeSkill(this.rows(), skillId));
  }

  protected primary(skillId: string): void {
    this.rowsChange.emit(setPrimary(this.rows(), skillId));
  }

  protected level(skillId: string, level: number): void {
    this.rowsChange.emit(setLevel(this.rows(), skillId, level));
  }
}
