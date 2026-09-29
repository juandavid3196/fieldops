import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { BranchDetail, BranchListItem } from '../../models/company-settings.model';
import { BranchDrawer } from './branch-drawer';

const API_BASE_URL = 'http://api.test';
const BRANCHES_URL = `${API_BASE_URL}/branches`;

const TIMEZONE_OPTIONS = [{ code: 'America/Chicago', label: 'America/Chicago' }];
const COUNTRY_OPTIONS = [{ code: 'US', label: 'United States' }];

const EXISTING_BRANCH: BranchDetail = {
  id: 'b-1',
  name: 'Austin Central',
  code: 'AUS-C',
  email: '',
  phone: '',
  addressLine1: '100 Main St',
  addressLine2: '',
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78701',
  countryCode: 'US',
  timezone: 'America/Chicago',
  businessHours: {},
  isActive: true,
  isMain: false,
  technicianCount: 0,
  servicePostalCodes: [],
  usesCompanyBilling: true,
  updatedAt: '2026-01-01T00:00:00.000000Z',
};

describe('BranchDrawer (AC-27, AC-36, AC-44)', () => {
  let fixture: ComponentFixture<BranchDrawer>;
  let host: HTMLElement;
  let component: BranchDrawer;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        ConfirmationService,
        MessageService,
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  function create(branchId: string | null): void {
    fixture = TestBed.createComponent(BranchDrawer);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('branchId', branchId);
    fixture.componentRef.setInput('canManage', true);
    fixture.componentRef.setInput('organizationTimezone', 'America/Chicago');
    fixture.componentRef.setInput('timezoneOptions', TIMEZONE_OPTIONS);
    fixture.componentRef.setInput('countryOptions', COUNTRY_OPTIONS);
    fixture.componentRef.setInput('branches', [] as readonly BranchListItem[]);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  const submitButton = (): HTMLButtonElement =>
    Array.from(host.querySelectorAll('button')).find(
      (button) =>
        button.textContent?.trim() === 'Create branch' ||
        button.textContent?.trim() === 'Update branch',
    ) as HTMLButtonElement;

  it('creates with the org timezone default (AC-27)', () => {
    create(null);
    let closed = false;
    let saved = false;
    component.closed.subscribe(() => (closed = true));
    component.saved.subscribe(() => (saved = true));

    expect(host.querySelector('.branch-drawer__title')?.textContent?.trim()).toBe('New branch');
    expect(component.form.controls.timezone.value).toBe('America/Chicago');

    component.form.patchValue({
      name: 'Cedar Park',
      code: 'CP',
      addressLine1: '400 Cedar Ave',
      city: 'Cedar Park',
      postalCode: '78613',
      countryCode: 'US',
    });
    submitButton().click();
    fixture.detectChanges();

    httpTesting
      .expectOne({ method: 'POST', url: BRANCHES_URL })
      .flush(
        { ...EXISTING_BRANCH, id: 'b-2', name: 'Cedar Park', code: 'CP' },
        { status: 201, statusText: 'Created' },
      );
    fixture.detectChanges();
    expect(closed).toBe(true);
    expect(saved).toBe(true);
  });

  it('edits a loaded branch, showing its name as the title (AC-44)', () => {
    create('b-1');
    let closed = false;
    let saved = false;
    component.closed.subscribe(() => (closed = true));
    component.saved.subscribe(() => (saved = true));
    httpTesting.expectOne({ method: 'GET', url: `${BRANCHES_URL}/b-1` }).flush(EXISTING_BRANCH);
    fixture.detectChanges();

    expect(host.querySelector('.branch-drawer__title')?.textContent?.trim()).toBe('Austin Central');
    expect(component.form.controls.code.value).toBe('AUS-C');

    const nameInput = host.querySelector<HTMLInputElement>('#branch-name')!;
    nameInput.value = 'Austin Central Renamed';
    nameInput.dispatchEvent(new Event('input'));
    submitButton().click();
    fixture.detectChanges();

    httpTesting
      .expectOne({ method: 'PUT', url: `${BRANCHES_URL}/b-1` })
      .flush({ ...EXISTING_BRANCH, name: 'Austin Central Renamed' });
    fixture.detectChanges();
    expect(closed).toBe(true);
    expect(saved).toBe(true);
  });

  it('opens new branches with the FR-16 defaults and sends normalized ZIP codes and billing inheritance (FR-14, FR-16, AC-15, AC-23)', () => {
    create(null);

    expect(component.form.controls.servicePostalCodes.value).toBe('');
    expect(component.form.controls.usesCompanyBilling.value).toBe(true);
    expect(component.form.controls.timezone.value).toBe('America/Chicago');
    expect(host.querySelectorAll('.business-hours__closed')).toHaveLength(7);
    expect(host.textContent).toContain('Separate ZIP codes with commas.');
    expect(host.textContent).toContain('Used in document numbering and reports.');
    expect(host.textContent).toContain(
      'Inherit tax rates, currency, and document numbering from company settings.',
    );
    expect(host.textContent).toContain(
      'Deactivating a branch prevents new records. All historical data is preserved.',
    );
    expect(host.querySelector('#branch-usesCompanyBilling')?.getAttribute('role')).toBe('switch');

    component.form.patchValue({
      name: 'Cedar Park',
      code: 'CP',
      addressLine1: '400 Cedar Ave',
      city: 'Cedar Park',
      postalCode: '78613',
      countryCode: 'US',
      servicePostalCodes: ' 78701, 78702,78701 ',
      usesCompanyBilling: false,
    });
    submitButton().click();
    fixture.detectChanges();

    const request = httpTesting.expectOne({ method: 'POST', url: BRANCHES_URL });
    expect(request.request.body).toMatchObject({
      servicePostalCodes: ['78701', '78702'],
      usesCompanyBilling: false,
    });
    request.flush(
      {
        status: 400,
        errors: { servicePostalCodes: ['Enter valid ZIP codes separated by commas.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(host.querySelector('#branch-servicePostalCodes-error')?.textContent?.trim()).toBe(
      'Enter valid ZIP codes separated by commas.',
    );
  });

  it.each([
    ['docked', { dock: true, md: true }],
    ['overlay', { dock: false, md: true }],
    ['fullscreen', { dock: false, md: false }],
  ] as const)('renders the %s presentation (FR-16, AC-22)', (mode, viewport) => {
    const original = window.matchMedia;
    window.matchMedia = ((query: string) => ({
      matches: query.includes('90rem') ? viewport.dock : viewport.md,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;

    try {
      create(null);
      expect(component.mode()).toBe(mode);
      const region = host.querySelector('aside[role="region"]');
      if (mode === 'docked') {
        expect(region?.getAttribute('aria-labelledby')).toBe('branch-drawer-title');
        expect(host.querySelector('[role="dialog"]')).toBeNull();
        let closed = false;
        component.closed.subscribe(() => (closed = true));
        region!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(closed).toBe(true);
      } else {
        expect(region).toBeNull();
        expect(host.querySelector('[role="dialog"]')?.getAttribute('aria-modal')).toBe('true');
      }
    } finally {
      window.matchMedia = original;
    }
  });

  it('maps a 409 duplicate code to the Code field and keeps the drawer open (AC-36)', () => {
    create(null);
    component.form.patchValue({
      name: 'Cedar Park',
      code: 'AUS-C',
      addressLine1: '400 Cedar Ave',
      city: 'Cedar Park',
      postalCode: '78613',
      countryCode: 'US',
    });
    submitButton().click();
    fixture.detectChanges();

    httpTesting
      .expectOne({ method: 'POST', url: BRANCHES_URL })
      .flush(
        { status: 409, errors: { code: ['Another branch already uses this code.'] } },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(host.querySelector('#branch-code-error')?.textContent?.trim()).toBe(
      'Another branch already uses this code.',
    );
    expect(host.querySelector('.branch-drawer__title')?.textContent?.trim()).toBe('New branch');
  });
});
