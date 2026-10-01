import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { PropertiesResponse, PropertyItem, RegionState } from '../../models/customer.model';
import { addressLine, firstLine, formatDateTime } from '../../utils/customer-detail-format';
import { formatDate } from '../../utils/customer-format';

interface PropertyView {
  readonly property: PropertyItem;
  readonly address: string;
  readonly branch: string;
  readonly instructions: string;
  readonly lastService: string;
  readonly nextAppointment: string;
}

const byOrder = (a: PropertyItem, b: PropertyItem): number =>
  Number(b.isPrimary) - Number(a.isPrimary) ||
  a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) ||
  a.id.localeCompare(b.id);

/**
 * Properties card (BR-04, BR-13). Presentational: the page owns data, the actions menu and the
 * property drawer.
 */
@Component({
  selector: 'app-properties-card',
  imports: [NgTemplateOutlet, ButtonDirective, Message, Skeleton, Tag],
  templateUrl: './properties-card.html',
  styleUrl: './properties-card.scss',
})
export class PropertiesCard {
  readonly state = input.required<RegionState<PropertiesResponse>>();
  readonly canMutate = input(false);
  /** Property whose action is in flight (its menu is locked). */
  readonly busyId = input<string | null>(null);

  readonly addRequested = output<void>();
  readonly viewRequested = output<PropertyItem>();
  readonly editRequested = output<PropertyItem>();
  readonly menuRequested = output<{ event: Event; property: PropertyItem }>();
  readonly retry = output<void>();

  readonly archivedOpen = signal(false);

  private readonly views = computed(() => {
    const data = this.state().data;
    const zone = data?.timezone ?? 'UTC';
    const toView = (property: PropertyItem): PropertyView => ({
      property,
      address: addressLine(property),
      branch: property.branch?.name ?? '—',
      instructions: property.serviceInstructions?.trim() || 'None',
      lastService: property.lastService
        ? [
            formatDate(property.lastService.completedAt, zone),
            firstLine(property.lastService.summary),
          ]
            .filter((part) => part.length > 0)
            .join(' · ')
        : 'None',
      nextAppointment: property.nextAppointment
        ? formatDateTime(property.nextAppointment.startsAt, zone)
        : 'None',
    });
    const items = data?.items ?? [];
    return {
      active: items
        .filter((item) => item.isActive)
        .sort(byOrder)
        .map(toView),
      archived: items
        .filter((item) => !item.isActive)
        .sort(byOrder)
        .map(toView),
    };
  });
  readonly active = computed(() => this.views().active);
  readonly archived = computed(() => this.views().archived);

  toggleArchived(): void {
    this.archivedOpen.update((open) => !open);
  }
}
