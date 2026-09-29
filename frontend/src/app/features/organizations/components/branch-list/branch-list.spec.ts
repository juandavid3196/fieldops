import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { BranchListItem } from '../../models/company-settings.model';
import { BranchList } from './branch-list';

const API_BASE_URL = 'http://api.test';

const BRANCHES: readonly BranchListItem[] = [
  {
    id: 'b-1',
    name: 'Austin Central',
    code: 'AUS-C',
    addressLine1: '100 Main St',
    addressLine2: '',
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78701',
    countryCode: 'US',
    timezone: 'America/Chicago',
    isActive: true,
    isMain: true,
    technicianCount: 1,
  },
  {
    id: 'b-2',
    name: 'North Austin',
    code: 'AUS-N',
    addressLine1: '200 Oak Ave',
    addressLine2: '',
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78729',
    countryCode: 'US',
    timezone: 'America/Chicago',
    isActive: false,
    isMain: false,
    technicianCount: 0,
  },
  {
    id: 'b-3',
    name: 'Round Rock',
    code: 'RR',
    addressLine1: '300 Elm Rd',
    addressLine2: '',
    city: 'Round Rock',
    stateRegion: 'TX',
    postalCode: '78664',
    countryCode: 'US',
    timezone: 'America/Chicago',
    isActive: true,
    isMain: false,
    technicianCount: 2,
  },
];

describe('BranchList (AC-26, AC-43)', () => {
  let fixture: ComponentFixture<BranchList>;
  let host: HTMLElement;
  let component: BranchList;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(),
        provideHttpClientTesting(),
        ConfirmationService,
        MessageService,
      ],
    });
    fixture = TestBed.createComponent(BranchList);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('branches', BRANCHES);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('loadError', null);
    fixture.componentRef.setInput('canManage', true);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  const rowNames = (): string[] =>
    Array.from(
      host.querySelectorAll('.branch-table__name'),
      (node) => node.textContent?.trim() ?? '',
    );

  it('filters by search and status, and toggles Branch-column sort with aria-sort', () => {
    expect(rowNames()).toEqual(['Austin Central', 'North Austin', 'Round Rock']);

    const search = host.querySelector<HTMLInputElement>('.branch-list__search')!;
    search.value = 'austin';
    search.dispatchEvent(new Event('input'));
    component.statusFilter.set('active');
    fixture.detectChanges();
    expect(rowNames()).toEqual(['Austin Central']);
    expect(host.querySelector('.branch-list__empty')).toBeNull();

    component.search.set('');
    component.statusFilter.set('all');
    fixture.detectChanges();
    const header = host.querySelector('.branch-table__sort-header')!;
    expect(header.getAttribute('aria-sort')).toBe('ascending');

    component.toggleSort();
    fixture.detectChanges();
    expect(component.sortDirection()).toBe('descending');
    expect(header.getAttribute('aria-sort')).toBe('descending');
    expect(rowNames()).toEqual(['Round Rock', 'North Austin', 'Austin Central']);

    component.toggleSort();
    fixture.detectChanges();
    expect(header.getAttribute('aria-sort')).toBe('ascending');

    component.search.set('nomatch');
    fixture.detectChanges();
    expect(host.querySelector('.branch-list__empty')?.textContent).toContain(
      'No branches match these filters.',
    );
  });

  it('offers row-menu items by role, activity and main flag, and highlights the selected row (FR-13, FR-15, FR-17, AC-17)', () => {
    const labels = (branch: BranchListItem): string[] =>
      component.buildMenu(branch).map((item) => item.label ?? '');
    const deactivate = component.buildMenu(BRANCHES[0]).find((item) => item.label === 'Deactivate');

    expect(labels(BRANCHES[0])).toEqual(['Edit', 'Deactivate']);
    expect(deactivate?.disabled).toBe(true);
    expect(deactivate?.tooltip).toBe("The main branch can't be deactivated.");

    // Active non-main but the only other active branch remains: Set as main is offered.
    expect(labels(BRANCHES[2])).toEqual(['Edit', 'Set as main branch', 'Deactivate']);
    expect(labels(BRANCHES[1])).toEqual(['Edit', 'Reactivate']);

    fixture.componentRef.setInput('canManage', false);
    expect(labels(BRANCHES[2])).toEqual(['View']);

    fixture.componentRef.setInput('selectedId', 'b-3');
    fixture.detectChanges();
    const selected = host.querySelectorAll('.branch-table__row--selected');
    expect(selected).toHaveLength(1);
    expect(selected[0].textContent).toContain('Round Rock');
    expect(host.querySelector('.branch-table__main-tag')?.textContent).toContain('Main branch');
  });
});
