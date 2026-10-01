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
import { ConfirmationService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { registerCompanyLeaveGuard } from '../../guards/register-company-leave.guard';
import { buildRegistrationRequest } from '../../models/organization-registration.model';
import { RegisterCompany } from './register-company';

const API_BASE_URL = 'http://api.test';
const REGISTER_URL = `${API_BASE_URL}/organization-registrations`;
const PASSWORD = 'correct horse battery';

@Component({ template: '<p>Sign in stub</p>' })
class SignInStub {}

describe('RegisterCompany wizard', () => {
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
          {
            path: 'register-company',
            component: RegisterCompany,
            canDeactivate: [registerCompanyLeaveGuard],
          },
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

  beforeEach(() => setup());

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    httpTesting.verify();
  });

  async function stable(): Promise<void> {
    await harness.fixture.whenStable();
  }

  const query = <T extends Element>(selector: string): T | null => host.querySelector<T>(selector);
  const title = (): string | undefined => query('h1')?.textContent?.trim();
  const eyebrow = (): string | undefined => query('.wizard__eyebrow')?.textContent?.trim();
  const primary = (): HTMLButtonElement =>
    query<HTMLButtonElement>('button[type="submit"]') as HTMLButtonElement;
  const buttonByText = (text: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(
      (button) => button.textContent?.trim() === text,
    );
  const buttonByName = (name: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(
      (button) => button.getAttribute('aria-label') === name,
    );
  const stepperButtons = (): HTMLButtonElement[] =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('nav[aria-label="Setup steps"] button'));
  const fieldError = (id: string): string | null =>
    query(`#${id}-error`)?.textContent?.trim() ?? null;
  const summaryText = (): string | null =>
    query('#register-company-error-summary')?.textContent ?? null;
  const focusedId = (): string | undefined => document.activeElement?.id;

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submitForm(): Promise<void> {
    query<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    await stable();
  }

  function fillStep1(): void {
    component.form.patchValue({
      organization: {
        name: 'Acme Field Services',
        legalName: 'Acme Field Services LLC',
        email: 'billing@acme.com',
        phone: '+1 555 111 2222',
        timezone: 'America/Chicago',
      },
      branch: {
        name: 'Main branch',
        code: 'main',
        timezone: 'America/Chicago',
        addressLine1: '123 Main St',
        city: 'Springfield',
        stateRegion: 'IL',
        postalCode: '62701',
        countryCode: 'US',
      },
    });
  }

  function fillStep3(): void {
    component.form.patchValue({
      owner: {
        firstName: 'jane',
        lastName: 'doe',
        email: 'owner@acme.com',
        password: PASSWORD,
      },
      confirmPassword: PASSWORD,
    });
  }

  async function advanceToReview(): Promise<void> {
    fillStep1();
    fillStep3();
    for (let index = 0; index < 3; index++) {
      await submitForm();
    }
  }

  async function createOrganization(): Promise<TestRequest> {
    await advanceToReview();
    await submitForm();
    return httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });
  }

  it('navigates per step with validation, Back, stepper, Edit and time zone follow (AC-01 to AC-06, AC-12)', async () => {
    // AC-01: initial render, no request (afterEach verifies), no Back, only step 1 selectable.
    expect(eyebrow()).toBe('STEP 1 OF 4');
    expect(title()).toBe('Create your organization');
    expect(primary().textContent).toContain('Continue');
    expect(buttonByText('Back')).toBeUndefined();
    expect(host.textContent).toContain('You can review everything before creating.');
    expect(stepperButtons()).toHaveLength(1);
    expect(query('nav[aria-label="Setup steps"] [aria-current="step"]')).not.toBeNull();

    // AC-05: branch time zone follows until it is edited.
    component.form.controls.organization.controls.timezone.setValue('America/New_York');
    expect(component.form.controls.branch.controls.timezone.value).toBe('America/New_York');
    component.onBranchTimezoneChanged();
    component.form.controls.branch.controls.timezone.setValue('Europe/Madrid');
    component.form.controls.organization.controls.timezone.setValue('America/Chicago');
    expect(component.form.controls.branch.controls.timezone.value).toBe('Europe/Madrid');

    // AC-02: invalid step 1 stays, shows messages and the summary, focuses the first field.
    await submitForm();
    expect(eyebrow()).toBe('STEP 1 OF 4');
    expect(fieldError('organization-name')).toBe('This field is required.');
    expect(fieldError('branch-name')).toBe('This field is required.');
    expect(fieldError('owner-email')).toBeNull();
    expect(summaryText()).toContain('Display name: This field is required.');
    expect(focusedId()).toBe('organization-name');
    const name = query<HTMLInputElement>('#organization-name')!;
    type(name, 'Acme Field Services');
    name.dispatchEvent(new Event('blur'));
    await stable();
    expect(fieldError('organization-name')).toBeNull();

    // AC-03: valid step 1 advances, focuses the title and completes the step.
    fillStep1();
    await submitForm();
    expect(eyebrow()).toBe('STEP 2 OF 4');
    expect(title()).toBe('Business settings');
    expect(focusedId()).toBe('register-company-title');
    expect(stepperButtons().map((button) => button.getAttribute('aria-label'))).toEqual([
      'Company & branch, completed',
      'Business settings',
    ]);

    // AC-02 and AC-06: step 2 validates hours; closed days show Closed with disabled dropdowns.
    const hours = component.form.controls.branch.controls.businessHours.controls;
    expect(hours.sunday.controls.start.disabled).toBe(true);
    expect(host.textContent).toContain('Closed');
    hours.monday.patchValue({ start: '08:30', end: '08:30' });
    await submitForm();
    expect(eyebrow()).toBe('STEP 2 OF 4');
    expect(fieldError('branch-businessHours-monday-end')).toBe(
      'End time must be after start time.',
    );
    hours.monday.patchValue({ end: '17:00' });
    hours.sunday.controls.open.setValue(true);
    expect(hours.sunday.controls.start.enabled).toBe(true);
    hours.sunday.controls.open.setValue(false);
    await submitForm();
    expect(eyebrow()).toBe('STEP 3 OF 4');

    // AC-02 (step 3) and AC-04: invalid stays; Back keeps values; step 4 is not selectable.
    component.form.controls.owner.controls.password.setValue('short');
    fillStep3();
    component.form.controls.owner.controls.password.setValue('short');
    await submitForm();
    expect(eyebrow()).toBe('STEP 3 OF 4');
    expect(fieldError('owner-password')).toBe('Use 12 to 128 characters.');
    fillStep3();
    buttonByText('Back')!.click();
    await stable();
    expect(eyebrow()).toBe('STEP 2 OF 4');
    expect(stepperButtons()).toHaveLength(3);
    buttonByName('Owner account')!.click();
    await stable();
    expect(title()).toBe('Owner account');
    expect(query<HTMLInputElement>('#owner-password')!.value).toBe(PASSWORD);
    expect(query<HTMLInputElement>('#confirmPassword')!.value).toBe(PASSWORD);

    // Review reached; AC-12: Edit opens Back to review, invalid stays, valid returns to Review.
    await submitForm();
    expect(eyebrow()).toBe('STEP 4 OF 4');
    buttonByName('Edit Business settings')!.click();
    await stable();
    expect(eyebrow()).toBe('STEP 2 OF 4');
    expect(primary().textContent).toContain('Back to review');
    component.form.controls.organization.controls.quotePrefix.setValue('bad prefix!');
    await submitForm();
    expect(eyebrow()).toBe('STEP 2 OF 4');
    expect(fieldError('organization-quotePrefix')).toContain('Use 1–20 characters');
    component.form.controls.organization.controls.quotePrefix.setValue('qt');
    await submitForm();
    expect(eyebrow()).toBe('STEP 4 OF 4');
    expect(host.textContent).toContain('QT');
    expect(stepperButtons()).toHaveLength(4);
  });

  it('sends one request only from Create and routes server results (AC-13 to AC-17)', async () => {
    // AC-13: moving through the steps writes nothing to URL, storage or cookies.
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    await advanceToReview();
    expect(eyebrow()).toBe('STEP 4 OF 4');
    httpTesting.expectNone(REGISTER_URL);
    expect(setItem).not.toHaveBeenCalled();
    expect(document.cookie).not.toContain('Acme');
    expect(router.url).toBe('/register-company');

    // AC-14: a double activation sends one request with the builder body and locks the UI.
    await submitForm();
    await submitForm();
    const request = httpTesting.expectOne({ method: 'POST', url: REGISTER_URL });
    expect(request.request.body).toEqual(buildRegistrationRequest(component.form.getRawValue()));
    expect(JSON.stringify(request.request.body)).not.toContain('confirmPassword');
    expect(primary().textContent).toContain('Creating organization…');
    expect(primary().disabled).toBe(true);
    expect(buttonByText('Back')!.disabled).toBe(true);
    expect(buttonByName('Edit Owner account')!.disabled).toBe(true);
    expect(stepperButtons().every((button) => button.disabled)).toBe(true);

    // AC-15: 201 navigates replacing history without the leave dialog.
    const confirm = vi.spyOn(
      harness.routeDebugElement!.injector.get(ConfirmationService),
      'confirm',
    );
    request.flush({ organizationId: 'org-1' });
    await stable();
    expect(router.url).toBe('/auth/sign-in?registered=true');
    expect(confirm).not.toHaveBeenCalled();
  });

  it.each([
    {
      name: '400 with fields on step 1',
      status: 400,
      body: { errors: { 'organization.name': ['This field is required.'], 'owner.email': ['x'] } },
      step: 1,
      focus: 'organization-name',
      field: ['organization-name', 'This field is required.'],
    },
    {
      name: '400 with only business hours',
      status: 400,
      body: {
        errors: { 'branch.businessHours.monday.end': ['End time must be after start time.'] },
      },
      step: 2,
      focus: 'branch-businessHours-monday-end',
      field: ['branch-businessHours-monday-end', 'End time must be after start time.'],
    },
    {
      name: '409 duplicate owner email',
      status: 409,
      body: {
        errors: { 'owner.email': ['An account with this email already exists. Sign in instead.'] },
      },
      step: 3,
      focus: 'owner-email',
      field: ['owner-email', 'An account with this email already exists. Sign in instead.'],
    },
    {
      name: '429 with Retry-After',
      status: 429,
      headers: { 'Retry-After': '30' },
      locked: true,
    },
    { name: '500', status: 500 },
    {
      name: '400 without mappable keys',
      status: 400,
      body: { errors: { 'unknown.field': ['x'] } },
    },
    { name: 'network failure', status: 0 },
  ] as {
    name: string;
    status: number;
    body?: object;
    headers?: Record<string, string>;
    locked?: boolean;
    step?: number;
    focus?: string;
    field?: string[];
  }[])('handles $name and keeps every value (AC-16, AC-17)', async (scenario) => {
    const request = await createOrganization();
    if (scenario.locked) {
      vi.useFakeTimers();
    }
    if (scenario.status === 0) {
      request.error(new ProgressEvent('error'));
    } else {
      request.flush(
        { status: scenario.status, ...scenario.body },
        { status: scenario.status, statusText: 'Error', headers: scenario.headers },
      );
    }
    harness.fixture.detectChanges();
    await stable();

    const raw = component.form.getRawValue();
    expect(raw.owner.password).toBe(PASSWORD);
    expect(raw.confirmPassword).toBe(PASSWORD);
    expect(raw.organization.name).toBe('Acme Field Services');

    if (scenario.field) {
      // AC-16: earliest step with a mapped key opens, message on field and summary, focus field.
      expect(eyebrow()).toBe(`STEP ${scenario.step} OF 4`);
      expect(fieldError(scenario.field[0])).toBe(scenario.field[1]);
      expect(summaryText()).toContain(scenario.field[1]);
      expect(focusedId()).toBe(scenario.focus);
      expect(primary().textContent).toContain('Back to review');
      return;
    }

    // AC-17: Review stays open with the ApiError message focused; only 429 locks Create.
    expect(eyebrow()).toBe('STEP 4 OF 4');
    expect(query('.error-summary__message')?.textContent?.trim()).toBeTruthy();
    expect(focusedId()).toBe('register-company-error-summary');
    expect(primary().disabled).toBe(scenario.locked === true);
    if (scenario.locked) {
      vi.advanceTimersByTime(29_999);
      harness.fixture.detectChanges();
      expect(primary().disabled).toBe(true);
      vi.advanceTimersByTime(1);
      harness.fixture.detectChanges();
      expect(primary().disabled).toBe(false);
    }
  });

  it.each([{ changed: false }, { changed: true }])(
    'confirms leaving only when data changed, changed=$changed (AC-18)',
    async ({ changed }) => {
      const confirmation = harness.routeDebugElement!.injector.get(ConfirmationService);
      const confirm = vi.spyOn(confirmation, 'confirm');
      if (changed) {
        component.form.controls.organization.controls.name.setValue('Acme');
      }
      await stable();

      const unload = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(unload);
      expect(unload.defaultPrevented).toBe(changed);

      if (!changed) {
        await router.navigateByUrl('/auth/sign-in');
        expect(router.url).toBe('/auth/sign-in');
        expect(confirm).not.toHaveBeenCalled();
        return;
      }

      confirm.mockImplementationOnce((options) => {
        options.reject?.();
        return confirmation;
      });
      expect(await router.navigateByUrl('/auth/sign-in')).toBe(false);
      expect(router.url).toBe('/register-company');
      expect(component.form.controls.organization.controls.name.value).toBe('Acme');
      expect(confirm.mock.calls[0][0]).toMatchObject({
        key: 'discard-changes',
        message:
          'You have unsaved changes in your registration. If you leave now, those changes will be lost.',
      });

      confirm.mockImplementationOnce((options) => {
        options.accept?.();
        return confirmation;
      });
      expect(await router.navigateByUrl('/auth/sign-in')).toBe(true);
      expect(router.url).toBe('/auth/sign-in');
    },
  );

  it('toggles password visibility and renders the Review summary (AC-09, AC-11)', async () => {
    fillStep1();
    await submitForm();
    await submitForm();
    fillStep3();
    component.form.controls.owner.controls.password.setValue('OWNER@ACME.IO');
    component.form.controls.owner.controls.email.setValue('owner@acme.io');
    await stable();

    const password = query<HTMLInputElement>('#owner-password')!;
    const confirmField = query<HTMLInputElement>('#confirmPassword')!;
    expect(password.type).toBe('password');
    expect(query('#owner-password-requirements')?.textContent).toContain('12–128 characters (met)');
    expect(query('#owner-password-requirements')?.textContent).toContain(
      'Must not match your email (not met)',
    );
    query<HTMLInputElement>('#owner-show-password')!.click();
    await stable();
    expect(password.type).toBe('text');
    expect(confirmField.type).toBe('text');

    component.form.patchValue({
      owner: { password: PASSWORD, email: 'owner@acme.com' },
      confirmPassword: PASSWORD,
    });
    await submitForm();

    const text = host.textContent ?? '';
    expect(eyebrow()).toBe('STEP 4 OF 4');
    expect(text).toContain('Everything is ready');
    for (const label of [
      'Display name',
      'Legal business name',
      'Company time zone',
      'Branch code',
      'Address',
      'Business hours',
      'Default currency',
      'Next invoice number',
      'Owner',
    ]) {
      expect(text).toContain(label);
    }
    expect(text).toContain('Springfield, IL 62701');
    expect(text).toContain('United States');
    expect(text).toContain('USD — US Dollar');
    expect(text).toContain('America/Chicago');
    expect(text).toContain('Mon – Fri, 8:00 AM – 5:00 PM · Sat, 9:00 AM – 1:00 PM');
    expect(text.match(/Not provided/g)).toHaveLength(4);
    expect(text).toContain('JD');
    expect(text).not.toContain(PASSWORD);
    expect(['Company profile', 'First branch', 'Business settings', 'Owner account']).toEqual(
      [
        'Edit Company profile',
        'Edit First branch',
        'Edit Business settings',
        'Edit Owner account',
      ].map((name) => buttonByName(name)?.getAttribute('aria-label')?.replace('Edit ', '')),
    );
  });
});
