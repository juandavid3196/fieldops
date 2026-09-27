import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { WEEKDAYS } from '../../models/organization-registration.model';
import { RegisterCompany } from './register-company';

const API_BASE_URL = 'http://api.test';
const REGISTER_URL = `${API_BASE_URL}/organization-registrations`;

@Component({ template: '<p>Sign in stub</p>' })
class SignInStub {}

describe('RegisterCompany', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let host: HTMLElement;
  let component: RegisterCompany;

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          { path: 'register-company', component: RegisterCompany },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    await harness.navigateByUrl('/register-company', RegisterCompany);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
    component = harness.routeDebugElement!.componentInstance as RegisterCompany;
  }

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  async function stable(): Promise<void> {
    await harness.fixture.whenStable();
  }

  const query = <T extends Element>(selector: string): T | null => host.querySelector<T>(selector);
  const submitButton = () => query<HTMLButtonElement>('button[type="submit"]') as HTMLButtonElement;
  const fieldError = (id: string): string | null =>
    query(`#${id}-error`)?.textContent?.trim() ?? null;
  const pageMessageText = (): string | null =>
    query('.error-summary__message')?.textContent?.trim() ?? null;

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submitForm(): Promise<void> {
    query<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    await stable();
  }

  function fillValidForm(): void {
    component.form.patchValue({
      organization: {
        name: 'Acme Field Services',
        legalName: 'Acme Field Services LLC',
        taxId: '',
        email: 'billing@acme.com',
        phone: '+1 555 111 2222',
        timezone: 'America/Chicago',
        currency: 'USD',
        defaultTaxRate: 0,
        quotePrefix: 'Q',
        workOrderPrefix: 'WO',
        invoicePrefix: 'INV',
        nextInvoiceNumber: 1,
      },
      branch: {
        name: 'Main branch',
        code: 'MAIN',
        phone: '',
        email: '',
        timezone: 'America/Chicago',
        addressLine1: '123 Main St',
        city: 'Springfield',
        stateRegion: 'IL',
        postalCode: '62701',
        countryCode: 'US',
      },
      owner: {
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'owner@acme.com',
        phone: '',
        password: 'correct horse battery',
      },
      confirmPassword: 'correct horse battery',
    });
  }

  async function submitValid(): Promise<TestRequest> {
    fillValidForm();
    await submitForm();
    return httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });
  }

  describe('default render (AC-01)', () => {
    beforeEach(() => setup());

    it('renders the five sections in order with no auth prompt', () => {
      const headings = Array.from(host.querySelectorAll('h2')).map((h) => h.textContent?.trim());
      expect(headings).toEqual([
        'Company profile',
        'First branch',
        'Taxes & currency',
        'Document numbering',
        'Owner account',
      ]);
      expect(query('h1')?.textContent).toContain('Create your organization');
      expect(submitButton().textContent?.trim()).toBe('Create organization');
      expect(host.textContent).not.toContain('Sign in');
    });

    it('shows the BR-02 defaults', () => {
      const value = component.form.getRawValue();
      expect(value.organization.currency).toBe('USD');
      expect(value.organization.defaultTaxRate).toBe(0);
      expect(value.organization.quotePrefix).toBe('Q');
      expect(value.organization.workOrderPrefix).toBe('WO');
      expect(value.organization.invoicePrefix).toBe('INV');
      expect(value.organization.nextInvoiceNumber).toBe(1);
      expect(value.branch.businessHours.monday).toEqual({
        open: true,
        start: '08:00',
        end: '17:00',
      });
      expect(value.branch.businessHours.saturday).toEqual({
        open: true,
        start: '09:00',
        end: '13:00',
      });
      expect(value.branch.businessHours.sunday.open).toBe(false);
    });
  });

  describe('empty submit (AC-02, AC-03)', () => {
    beforeEach(() => setup());

    it('sends no request, shows rule-1 messages and focuses the first invalid field', async () => {
      await submitForm();

      httpTesting.expectNone(REGISTER_URL);
      expect(fieldError('organization-name')).toBe('This field is required.');
      expect(fieldError('organization-legalName')).toBe('This field is required.');
      expect(fieldError('organization-email')).toBe('This field is required.');
      expect(fieldError('branch-name')).toBe('This field is required.');
      expect(fieldError('owner-email')).toBe('This field is required.');
      expect(document.activeElement?.id).toBe('organization-name');
      expect(query('#register-company-error-summary')).not.toBeNull();
    });
  });

  describe('blur clears a corrected field (AC-04)', () => {
    beforeEach(() => setup());

    it('clears the error when the field is corrected and blurred after a failed submit', async () => {
      await submitForm();
      expect(fieldError('organization-name')).toBe('This field is required.');

      const input = query<HTMLInputElement>('#organization-name')!;
      type(input, 'Acme Field Services');
      input.dispatchEvent(new Event('blur'));
      await stable();

      expect(fieldError('organization-name')).toBeNull();
    });
  });

  describe('submitting (AC-21)', () => {
    beforeEach(() => setup());

    it('shows the submitting state, locks inputs and sends exactly one request', async () => {
      const request = await submitValid();

      expect(submitButton().textContent?.trim()).toBe('Creating organization…');
      expect(submitButton().disabled).toBe(true);
      expect(query<HTMLInputElement>('#organization-name')?.readOnly).toBe(true);
      expect(query<HTMLInputElement>('#owner-password')?.readOnly).toBe(true);

      submitButton().click();
      await submitForm();
      httpTesting.expectNone(REGISTER_URL);

      request.flush({ organizationId: 'org-1' });
      await stable();
    });
  });

  describe('success (AC-22)', () => {
    beforeEach(() => setup());

    it('navigates to /auth/sign-in?registered=true on 201', async () => {
      const request = await submitValid();
      request.flush({ organizationId: 'org-1' });
      await stable();

      expect(router.url).toBe('/auth/sign-in?registered=true');
    });
  });

  describe('409 duplicate email (AC-23, AC-27)', () => {
    beforeEach(() => setup());

    it('shows the duplicate email message on owner.email and keeps other values', async () => {
      fillValidForm();
      await submitForm();
      const request = httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });

      request.flush(
        {
          status: 409,
          errors: {
            'owner.email': ['An account with this email already exists. Sign in instead.'],
          },
        },
        { status: 409, statusText: 'Conflict' },
      );
      await stable();

      expect(fieldError('owner-email')).toBe(
        'An account with this email already exists. Sign in instead.',
      );
      expect(query<HTMLInputElement>('#organization-name')?.value).toBe('Acme Field Services');
      expect(query<HTMLInputElement>('#owner-password')?.value).toBe('');
      expect(pageMessageText()).toBeNull();
    });
  });

  describe('429 rate limit (AC-24)', () => {
    beforeEach(() => setup());

    it('disables submit for Retry-After seconds, then enables it', async () => {
      const request = await submitValid();
      vi.useFakeTimers();

      request.flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '30' },
      });
      harness.fixture.detectChanges();

      expect(submitButton().disabled).toBe(true);
      vi.advanceTimersByTime(29_999);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(true);
      vi.advanceTimersByTime(1);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(false);
    });
  });

  describe('500 and network errors (AC-25)', () => {
    beforeEach(() => setup());

    it('shows the summary message, unlocks the form and clears passwords on 500', async () => {
      const request = await submitValid();
      request.flush({ title: 'x', status: 500 }, { status: 500, statusText: 'Error' });
      await stable();

      expect(pageMessageText()).toBe('An unexpected error occurred. Please try again later.');
      expect(submitButton().disabled).toBe(false);
      expect(query<HTMLInputElement>('#owner-password')?.value).toBe('');
    });

    it('shows the summary message on a network error and unlocks the form', async () => {
      const request = await submitValid();
      request.error(new ProgressEvent('error'));
      await stable();

      expect(pageMessageText()).toBe(
        'Unable to reach the server. Check your connection and try again.',
      );
      expect(submitButton().disabled).toBe(false);
    });
  });

  describe('non-catalog server message (AC-26)', () => {
    beforeEach(() => setup());

    it('replaces a message outside BR-24 with "Enter a valid value."', async () => {
      const request = await submitValid();
      request.flush(
        {
          status: 400,
          errors: { 'organization.name': ['The JSON value could not be converted.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await stable();

      expect(fieldError('organization-name')).toBe('Enter a valid value.');
    });
  });

  describe('branch time zone follows the company time zone (FR-14)', () => {
    beforeEach(() => setup());

    it('AC-28: updates the branch time zone when it was never edited', async () => {
      component.form.controls.organization.controls.timezone.setValue('America/New_York');
      await stable();

      expect(component.form.controls.branch.controls.timezone.value).toBe('America/New_York');
    });

    it('AC-29: keeps the edited branch time zone', async () => {
      component.onBranchTimezoneChanged();
      component.form.controls.branch.controls.timezone.setValue('Europe/Madrid');

      component.form.controls.organization.controls.timezone.setValue('America/New_York');
      await stable();

      expect(component.form.controls.branch.controls.timezone.value).toBe('Europe/Madrid');
    });
  });

  describe('business hours payload (AC-30, FR-15)', () => {
    beforeEach(() => setup());

    it('sends only open days as {start,end} and omits closed days', async () => {
      fillValidForm();
      for (const day of WEEKDAYS) {
        component.form.controls.branch.controls.businessHours.controls[day].controls.open.setValue(
          day === 'monday',
        );
      }
      component.form.controls.branch.controls.businessHours.controls.monday.patchValue({
        start: '08:00',
        end: '17:00',
      });
      await submitForm();

      const request = httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });
      expect(request.request.body.branch.businessHours).toEqual({
        monday: { start: '08:00', end: '17:00' },
      });
      request.flush({ organizationId: 'org-1' });
      await stable();
    });

    it('sends {} when every day is closed', async () => {
      fillValidForm();
      for (const day of WEEKDAYS) {
        component.form.controls.branch.controls.businessHours.controls[day].controls.open.setValue(
          false,
        );
      }
      await submitForm();

      const request = httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });
      expect(request.request.body.branch.businessHours).toEqual({});
      request.flush({ organizationId: 'org-1' });
      await stable();
    });
  });

  describe('business hours validation (AC-31)', () => {
    beforeEach(() => setup());

    it('shows the end-not-after-start message', async () => {
      fillValidForm();
      component.form.controls.branch.controls.businessHours.controls.monday.patchValue({
        start: '09:00',
        end: '09:00',
      });
      await submitForm();

      httpTesting.expectNone(REGISTER_URL);
      expect(fieldError('branch-businessHours-monday-end')).toBe(
        'End time must be after start time.',
      );
    });
  });

  describe('currency list (AC-40)', () => {
    beforeEach(() => setup());

    it('includes USD and excludes XAU and codes outside BR-09', () => {
      const codes = component.currencyOptionsList.map((option) => option.code);
      expect(codes).toContain('USD');
      expect(codes).not.toContain('XAU');
      expect(codes).not.toContain('AAA');
    });
  });
});
