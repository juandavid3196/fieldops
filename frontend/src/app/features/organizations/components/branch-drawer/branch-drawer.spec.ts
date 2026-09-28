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
