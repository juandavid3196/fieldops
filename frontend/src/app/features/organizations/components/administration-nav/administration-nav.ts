import { Component, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';

interface AdministrationItem {
  readonly label: string;
  readonly icon: string;
  /** Section id on the Company setup page (anchor item), or `null` for a route item. */
  readonly section: string | null;
  readonly link: string | null;
  /** The page this item stands for, when it is a real Administration page. */
  readonly page?: AdministrationPage;
}

export type AdministrationPage = 'company' | 'users' | 'products';

const COMPANY_PATH = '/admin/company';

/** BR-03: items in order. Anchors scroll to a section; the rest open their page or Coming soon page. */
export const ADMINISTRATION_ITEMS: readonly AdministrationItem[] = [
  { label: 'Company profile', icon: 'pi-building', section: 'company-profile', link: null },
  { label: 'Branches', icon: 'pi-map-marker', section: 'branches', link: null },
  {
    label: 'Business hours',
    icon: 'pi-clock',
    section: null,
    link: comingSoonPath('business-hours'),
  },
  {
    label: 'Users & permissions',
    icon: 'pi-user-edit',
    section: null,
    link: '/admin/users',
    page: 'users',
  },
  {
    label: 'Products & services',
    icon: 'pi-box',
    section: null,
    link: '/admin/products-services',
    page: 'products',
  },
  { label: 'Taxes & currency', icon: 'pi-dollar', section: 'taxes-currency', link: null },
  { label: 'Document numbering', icon: 'pi-hashtag', section: 'document-numbering', link: null },
  { label: 'Notifications', icon: 'pi-bell', section: null, link: comingSoonPath('notifications') },
];

/**
 * Administration column (FR-05): 224px from 1100px, a horizontal scrollable link row below.
 * On the Company setup page the current anchor follows the last selection (AS-05, no
 * scroll-spy); on the Users page the anchors link back to Company setup and Users is current.
 */
@Component({
  selector: 'app-administration-nav',
  imports: [RouterLink],
  templateUrl: './administration-nav.html',
  styleUrl: './administration-nav.scss',
})
export class AdministrationNav {
  readonly current = input<AdministrationPage>('company');

  readonly items = ADMINISTRATION_ITEMS;
  readonly companyPath = COMPANY_PATH;
  readonly activeSection = signal('company-profile');

  select(event: Event, section: string): void {
    event.preventDefault();
    this.activeSection.set(section);
    document.getElementById(section)?.scrollIntoView({ block: 'start' });
  }
}
