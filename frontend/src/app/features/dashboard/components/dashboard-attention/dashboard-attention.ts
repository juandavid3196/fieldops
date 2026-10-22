import { Component, computed, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { TranslocoPipe } from '@jsverse/transloco';

import { Attention } from '../../models/dashboard.model';
import { AppLink } from '../../utils/dashboard-params';
import { AttentionView, attentionView } from '../../utils/dashboard-view';

import { t } from '../../../../core/i18n/translate';
export const ATTENTION_COLLAPSED_COUNT = 4;
export const NOTHING_NEEDS_ATTENTION = (): string =>
  t('dashboard.dashboardAttention.nothingNeedsAttention');

/** BR-11: delayed, unassigned, over-estimate and over-capacity items. */
@Component({
  selector: 'app-dashboard-attention',
  imports: [TranslocoPipe, Menu, RouterLink],
  templateUrl: './dashboard-attention.html',
  styleUrl: './dashboard-attention.scss',
})
export class DashboardAttention {
  private readonly router = inject(Router);
  private readonly section = viewChild.required<ElementRef<HTMLElement>>('section');
  private readonly menu = viewChild.required<Menu>('menu');

  readonly items = input.required<readonly Attention[]>();
  readonly date = input.required<string>();
  /** Needs attention lists the selected day only when it is today or past. */
  readonly isFuture = input.required<boolean>();
  readonly timezone = input.required<string>();

  readonly expanded = signal(false);
  readonly menuModel = signal<MenuItem[]>([]);
  get empty(): string {
    return NOTHING_NEEDS_ATTENTION();
  }

  private readonly all = computed<readonly AttentionView[]>(() =>
    this.isFuture()
      ? []
      : this.items().map((item, index) => attentionView(item, index, this.date(), this.timezone())),
  );
  readonly total = computed(() => this.all().length);
  readonly shown = computed(() =>
    this.expanded() ? this.all() : this.all().slice(0, ATTENTION_COLLAPSED_COUNT),
  );
  readonly canExpand = computed(() => this.total() > ATTENTION_COLLAPSED_COUNT);

  /** The "Review" KPI link focuses this card. */
  focus(): void {
    const element = this.section().nativeElement;
    element.scrollIntoView?.({ block: 'nearest' });
    element.focus();
  }

  openMenu(event: Event, item: AttentionView): void {
    const link: AppLink = item.menu.link;
    this.menuModel.set([
      {
        label: item.menu.label,
        icon: 'pi pi-eye',
        command: () => void this.router.navigate([...link.path], { queryParams: link.queryParams }),
      },
    ]);
    this.menu().toggle(event);
  }
}
