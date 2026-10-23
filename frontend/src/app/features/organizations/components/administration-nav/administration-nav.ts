import { Component, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { localizedList, t } from '../../../../core/i18n/translate';
interface AdministrationItem {
  readonly label: string;
  readonly icon: string;
  /** Section id on the Company setup page (anchor item), or `null` for a route item. */
  readonly section: string | null;
  readonly link: string | null;
  /** The page this item stands for, when it is a real Administration page. */
  readonly page?: AdministrationPage;
}

const COLLAPSED_KEY = 'fieldops.admin-nav.collapsed';

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

export type AdministrationPage = 'company' | 'users' | 'products' | 'business-hours';

const COMPANY_PATH = '/admin/company';

/** BR-03: items in order. Anchors scroll to a section; the rest open their page or Coming soon page. */
export const ADMINISTRATION_ITEMS: readonly AdministrationItem[] = localizedList(() => [
  {
    label: t('organizations.administrationNav.companyProfile'),
    icon: 'pi-building',
    section: 'company-profile',
    link: null,
  },
  {
    label: t('organizations.administrationNav.branches'),
    icon: 'pi-map-marker',
    section: 'branches',
    link: null,
  },
  {
    label: t('organizations.administrationNav.businessHours'),
    icon: 'pi-clock',
    section: null,
    link: '/admin/business-hours',
    page: 'business-hours',
  },
  {
    label: t('organizations.administrationNav.usersPermissions'),
    icon: 'pi-user-edit',
    section: null,
    link: '/admin/users',
    page: 'users',
  },
  {
    label: t('organizations.administrationNav.productsServices'),
    icon: 'pi-box',
    section: null,
    link: '/admin/products-services',
    page: 'products',
  },
  {
    label: t('organizations.administrationNav.taxesCurrency'),
    icon: 'pi-dollar',
    section: 'taxes-currency',
    link: null,
  },
  {
    label: t('organizations.administrationNav.documentNumbering'),
    icon: 'pi-hashtag',
    section: 'document-numbering',
    link: null,
  },
  {
    label: t('organizations.administrationNav.notifications'),
    icon: 'pi-bell',
    section: null,
    link: '/notifications',
  },
]);

/**
 * Administration column (FR-05): 224px from 1100px, a horizontal scrollable link row below.
 * On the Company setup page the current anchor follows the last selection (AS-05, no
 * scroll-spy); on the Users page the anchors link back to Company setup and Users is current.
 */
@Component({
  selector: 'app-administration-nav',
  imports: [TranslocoPipe, RouterLink],
  templateUrl: './administration-nav.html',
  styleUrl: './administration-nav.scss',
  host: { '[class.rail]': 'collapsed()' },
})
export class AdministrationNav {
  readonly current = input<AdministrationPage>('company');

  readonly items = ADMINISTRATION_ITEMS;
  readonly companyPath = COMPANY_PATH;
  readonly activeSection = signal('company-profile');
  /** Column shrunk to icons (from 1100px); the choice survives reloads. */
  readonly collapsed = signal(readCollapsed());

  toggle(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    try {
      localStorage.setItem(COLLAPSED_KEY, next ? '1' : '0');
    } catch {
      // Storage errors never break the layout.
    }
  }

  select(event: Event, section: string): void {
    event.preventDefault();
    this.activeSection.set(section);
    document.getElementById(section)?.scrollIntoView({ block: 'start' });
  }
}
