import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Tab, TabList, TabPanel, TabPanels, Tabs } from 'primeng/tabs';

import { PlannedMaterial, TechnicianVisitDetail } from '../../models/technician-visits.model';
import {
  materialLine,
  materialSourceLabel,
  materialsCountText,
  scopeLines,
  tasksSummary,
} from '../../utils/visit-detail';
import { PhotoOpenRequest, VisitPhoto, VisitPhotos } from '../visit-photos/visit-photos';

const OVERVIEW_MATERIALS = 3;

/** BR-16 read-only tabs: Overview, Tasks, Materials and Photos. */
@Component({
  selector: 'app-visit-tabs',
  imports: [
    NgTemplateOutlet,
    ButtonDirective,
    Tabs,
    TabList,
    Tab,
    TabPanels,
    TabPanel,
    VisitPhotos,
  ],
  templateUrl: './visit-tabs.html',
  styleUrl: './visit-tabs.scss',
})
export class VisitTabs {
  readonly visit = input.required<TechnicianVisitDetail>();
  readonly photos = input.required<readonly VisitPhoto[]>();
  readonly openPhoto = output<PhotoOpenRequest>();
  readonly openAssessment = output<HTMLElement>();

  readonly scope = computed(() => scopeLines(this.visit().scope));
  readonly overviewMaterials = computed(() =>
    this.visit().plannedMaterials.slice(0, OVERVIEW_MATERIALS),
  );
  readonly materialsCount = computed(() =>
    materialsCountText(this.visit().plannedMaterials.length),
  );
  readonly tasksText = computed(() => tasksSummary(this.visit().tasks));
  /** Decorative checklist progress; the "<n> of <m> tasks" text carries the meaning. */
  readonly progress = computed(() => {
    const tasks = this.visit().tasks;
    const completed = tasks.filter((task) => task.isCompleted).length;
    return tasks.length === 0 ? 0 : Math.round((completed / tasks.length) * 100);
  });
  readonly hasInstructions = computed(() => {
    const visit = this.visit();
    return visit.instructions !== null || visit.dispatchNote !== null;
  });

  line(material: PlannedMaterial): string {
    return materialLine(material);
  }

  source(material: PlannedMaterial): string {
    return materialSourceLabel(material.source);
  }
}
