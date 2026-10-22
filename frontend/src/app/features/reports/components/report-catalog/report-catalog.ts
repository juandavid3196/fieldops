import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { TranslocoPipe } from '@jsverse/transloco';

import { ReportInfo, ReportKey } from '../../models/reports.model';

import { t } from '../../../../core/i18n/translate';
interface CatalogGroup {
  readonly name: string;
  readonly count: number;
  readonly items: readonly ReportInfo[];
}

/** BR-01/BR-21: search, Favorites, Recently viewed and the report groups the caller may see. */
@Component({
  selector: 'app-report-catalog',
  imports: [TranslocoPipe, FormsModule, IconField, InputIcon, InputText],
  templateUrl: './report-catalog.html',
  styleUrl: './report-catalog.scss',
})
export class ReportCatalog {
  readonly reports = input.required<readonly ReportInfo[]>();
  readonly active = input<ReportKey | null>(null);
  readonly favorites = input<readonly ReportKey[]>([]);
  readonly recent = input<readonly ReportKey[]>([]);
  readonly disabled = input(false);

  readonly selected = output<ReportKey>();

  readonly search = signal('');

  private readonly matching = computed(() => {
    const term = this.search().trim().toLowerCase();
    return (info: ReportInfo): boolean =>
      term === '' ||
      info.name.toLowerCase().includes(term) ||
      info.description.toLowerCase().includes(term);
  });

  private pick(keys: readonly ReportKey[]): ReportInfo[] {
    const matches = this.matching();
    return keys
      .map((key) => this.reports().find((info) => info.key === key))
      .filter((info): info is ReportInfo => info !== undefined && matches(info));
  }

  readonly favoriteItems = computed(() => this.pick(this.favorites()));
  readonly recentItems = computed(() => this.pick(this.recent()));
  readonly groups = computed<CatalogGroup[]>(() => {
    const matches = this.matching();
    const names = [...new Set(this.reports().map((info) => info.group))];
    return names
      .map((name) => {
        const all = this.reports().filter((info) => info.group === name);
        return { name, count: all.length, items: all.filter(matches) };
      })
      .filter((group) => group.items.length > 0);
  });
  groupIcon(name: string): string {
    return name.startsWith(t('reports.reportCatalog.billing'))
      ? 'pi-wallet'
      : name.startsWith(t('reports.reportCatalog.sales'))
        ? 'pi-file'
        : 'pi-cog';
  }

  readonly noMatches = computed(() => this.search().trim() !== '' && this.groups().length === 0);
}
