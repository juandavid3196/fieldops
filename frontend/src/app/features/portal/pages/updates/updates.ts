import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { tap } from 'rxjs';

import { updateLink } from '../../components/updates-card/updates-card';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalUpdate, UpdatesResponse } from '../../models/portal.model';
import { PortalUpdatesStore } from '../../services/portal-updates.store';
import { updateTimeLabel } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';

type UpdateTone = 'success' | 'info' | 'blue' | 'amber';

interface UpdateStyle {
  readonly icon: string;
  readonly tone: UpdateTone;
  readonly action: string;
}

/** Icon, tone and action label of each BR-24 event type (Design 28 visual system). */
const UPDATE_STYLES: Readonly<Record<string, UpdateStyle>> = {
  receipt_available: { icon: 'pi-file', tone: 'success', action: 'View receipt' },
  invoice_sent: { icon: 'pi-file', tone: 'info', action: 'View invoice' },
  visit_completed: { icon: 'pi-check-circle', tone: 'success', action: 'View report' },
  on_the_way: { icon: 'pi-calendar', tone: 'blue', action: 'View appointment' },
  technician_assigned: { icon: 'pi-user', tone: 'blue', action: 'View appointment' },
  quote_sent: { icon: 'pi-file-edit', tone: 'amber', action: 'Review quote' },
};

const FALLBACK_STYLE: UpdateStyle = { icon: 'pi-bell', tone: 'blue', action: 'View' };

/** Client-side filters over the loaded feed; they never change what is fetched or marked seen. */
export const UPDATE_FILTERS = [
  { id: 'all', label: 'All', types: null },
  { id: 'action', label: 'Action required', types: ['quote_sent'] },
  {
    id: 'appointments',
    label: 'Appointments',
    types: ['technician_assigned', 'on_the_way', 'visit_completed'],
  },
  { id: 'billing', label: 'Billing', types: ['invoice_sent', 'receipt_available'] },
] as const;
export type UpdateFilterId = (typeof UPDATE_FILTERS)[number]['id'];

export const UPDATE_GROUPS = ['Today', 'Yesterday', 'Earlier'] as const;
export type UpdateGroup = (typeof UPDATE_GROUPS)[number];

/** Local-calendar group of an event: Today, Yesterday or Earlier. */
export function updateGroup(occurredAt: string, now: Date): UpdateGroup {
  const at = new Date(occurredAt);
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const day = new Date(at.getFullYear(), at.getMonth(), at.getDate()).getTime();
  if (day >= today) {
    return 'Today';
  }
  return day >= today - 86_400_000 ? 'Yesterday' : 'Earlier';
}

/** All updates (BR-24, BR-25): opening the page marks them seen, so the bell shows no count. */
@Component({
  selector: 'app-portal-updates',
  imports: [
    ButtonDirective,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    PortalState,
    RouterLink,
    ToggleSwitch,
  ],
  templateUrl: './updates.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss', './updates.scss'],
})
export class PortalUpdates {
  private readonly store = inject(PortalUpdatesStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly resource = new PortalResource<UpdatesResponse>(
    (response) => response.items.length === 0,
  );

  readonly filters = UPDATE_FILTERS;
  readonly filter = signal<UpdateFilterId>('all');
  readonly search = signal('');
  readonly unreadOnly = signal(false);

  readonly total = computed(() => this.resource.data()?.items.length ?? 0);
  readonly unread = computed(() => this.resource.data()?.unreadCount ?? 0);

  readonly groups = computed(() => {
    const now = new Date();
    const types: readonly string[] | null =
      UPDATE_FILTERS.find((option) => option.id === this.filter())?.types ?? null;
    const term = this.search().trim().toLowerCase();
    const unreadOnly = this.unreadOnly();
    const visible = (this.resource.data()?.items ?? []).filter(
      (update) =>
        (types === null || types.includes(update.type)) &&
        (!unreadOnly || update.isUnread) &&
        (term === '' || `${update.title} ${update.subtitle}`.toLowerCase().includes(term)),
    );
    const rows = visible.map((update: PortalUpdate) => ({
      update,
      style: UPDATE_STYLES[update.type] ?? FALLBACK_STYLE,
      link: updateLink(update),
      time: updateTimeLabel(update.occurredAt, now),
      group: updateGroup(update.occurredAt, now),
    }));
    return UPDATE_GROUPS.map((label) => ({
      label,
      rows: rows.filter((row) => row.group === label),
    })).filter((group) => group.rows.length > 0);
  });

  readonly filtered = computed(
    () => this.filter() !== 'all' || this.unreadOnly() || this.search().trim() !== '',
  );

  constructor() {
    this.load();
  }

  load(): void {
    this.resource.load(
      this.store.load().pipe(
        // Seen after the list is loaded, so the unread dots of this visit stay visible.
        tap(() =>
          this.store
            .markSeen()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ error: () => undefined }),
        ),
      ),
    );
  }
}
