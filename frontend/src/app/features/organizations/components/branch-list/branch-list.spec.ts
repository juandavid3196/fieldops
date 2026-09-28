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
      host.querySelectorAll('.branch-list__name'),
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
    const header = host.querySelector('.branch-list__sort-header')!;
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
});
