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
import axe from 'axe-core';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { SignIn } from './sign-in';

const API_BASE_URL = 'http://api.test';
const SIGN_IN_URL = `${API_BASE_URL}/sessions`;

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
};

const GENERIC_MESSAGE = "We couldn't sign you in right now. Try again in a moment.";
const INVALID_CREDENTIALS = 'The email or password is incorrect. Check your details and try again.';

@Component({ template: '<p>Overview stub</p>' })
class OverviewStub {}

describe('SignIn', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let host: HTMLElement;

  async function setup(url = '/auth/sign-in'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          { path: 'auth/sign-in', component: SignIn },
          { path: 'overview', component: OverviewStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    await harness.navigateByUrl(url, SignIn);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
  }

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  async function stable(): Promise<void> {
    await harness.fixture.whenStable();
  }

  const query = <T extends Element>(selector: string): T | null => host.querySelector<T>(selector);
  const emailInput = () => query<HTMLInputElement>('#email') as HTMLInputElement;
  const passwordInput = () => query<HTMLInputElement>('#password') as HTMLInputElement;
  const submitButton = () => query<HTMLButtonElement>('button[type="submit"]') as HTMLButtonElement;
  const toggleButton = () => query<HTMLButtonElement>('.sign-in__toggle') as HTMLButtonElement;
  const emailError = () => query('#email-error')?.textContent?.trim() ?? null;
  const passwordError = () => query('#password-error')?.textContent?.trim() ?? null;
  const pageMessage = () => query('p-message');

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function blur(input: HTMLInputElement): void {
    input.dispatchEvent(new Event('blur'));
  }

  async function submitForm(): Promise<void> {
    query<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    await stable();
  }

  async function submitValid(
    email = 'sofia@example.com',
    password = 'correct horse',
  ): Promise<TestRequest> {
    type(emailInput(), email);
    type(passwordInput(), password);
    await submitForm();
    return httpTesting.expectOne({ method: 'POST', url: SIGN_IN_URL });
  }

  async function respond(
    request: TestRequest,
    status: number,
    body: object | null = null,
    headers: Record<string, string> = {},
  ): Promise<void> {
    request.flush(body, { status, statusText: 'Error', headers });
    await stable();
  }

  describe('default state (AC-01, AC-51)', () => {
    beforeEach(() => setup());

    it('renders the form controls with their attributes', () => {
      expect(query('h1')?.textContent).toContain('Welcome back');
      expect(host.textContent).toContain('Sign in to continue to FieldOps');
      expect(emailInput().type).toBe('email');
      expect(emailInput().getAttribute('autocomplete')).toBe('email');
      expect(query('label[for="email"]')?.textContent).toContain('Email');
      expect(passwordInput().type).toBe('password');
      expect(passwordInput().getAttribute('autocomplete')).toBe('current-password');
      expect(query('label[for="password"]')?.textContent).toContain('Password');
      expect(query('p-checkbox')).not.toBeNull();
      expect(query('label[for="remember-me"]')?.textContent).toContain('Remember me');
      expect(query<HTMLInputElement>('#remember-me')?.checked).toBe(false);
      expect(submitButton().textContent?.trim()).toBe('Sign in');
      expect(submitButton().disabled).toBe(false);
      expect(query('#other-ways-heading')?.textContent).toContain('Other ways to get started');
      expect(pageMessage()).toBeNull();
    });

    it('uses the Remember me label as the 44px touch target for the checkbox (FR-20)', async () => {
      const label = query<HTMLLabelElement>('.sign-in__remember > .sign-in__remember-label');
      expect(label?.htmlFor).toBe('remember-me');
      expect(label?.textContent?.trim()).toBe('Remember me');
      expect(label?.control).toBe(query('#remember-me'));

      label?.click();
      await stable();
      expect(query<HTMLInputElement>('#remember-me')?.checked).toBe(true);
    });

    it('links "Create a company account" to /auth/register-company (AC-51)', () => {
      const link = query<HTMLAnchorElement>('.sign-in__link');
      expect(link?.textContent).toContain('Create a company account');
      expect(link?.textContent).toContain('For service business owners');
      expect(link?.getAttribute('href')).toBe('/auth/register-company');
    });

    it('omits every control the spec excludes', () => {
      const text = host.textContent ?? '';
      for (const omitted of [
        'Need service?',
        'Request a service',
        'Forgot password?',
        'Accept an invitation',
        'Guest requests',
        'Privacy',
        'Terms',
        'Help',
        'English',
      ]) {
        expect(text).not.toContain(omitted);
      }
      expect(host.querySelectorAll('a')).toHaveLength(1);
      expect(host.querySelector('i.pi, [class*="pi-"]')).toBeNull();
      expect(host.querySelector('[autofocus]')).toBeNull();
    });

    it('renders one h1 and the brand panel heading as h2', () => {
      expect(host.querySelectorAll('h1')).toHaveLength(1);
      expect(query('#brand-heading')?.tagName).toBe('H2');
    });

    it('toggles the password visibility with label and aria-pressed', async () => {
      expect(toggleButton().type).toBe('button');
      expect(toggleButton().textContent?.trim()).toBe('Show');
      expect(toggleButton().getAttribute('aria-label')).toBe('Show password');
      expect(toggleButton().getAttribute('aria-pressed')).toBe('false');
      expect(toggleButton().getAttribute('aria-controls')).toBe('password');

      toggleButton().click();
      await stable();

      expect(passwordInput().type).toBe('text');
      expect(toggleButton().textContent?.trim()).toBe('Hide');
      expect(toggleButton().getAttribute('aria-label')).toBe('Hide password');
      expect(toggleButton().getAttribute('aria-pressed')).toBe('true');

      toggleButton().click();
      await stable();

      expect(passwordInput().type).toBe('password');
      expect(toggleButton().getAttribute('aria-pressed')).toBe('false');
    });
  });

  describe('registered message (AC-04)', () => {
    it('shows the success message with role="status" and removes the query parameter', async () => {
      await setup('/auth/sign-in?registered=true');

      const message = pageMessage();
      expect(message?.textContent).toContain('Your organization was created. Sign in to continue.');
      expect(message?.getAttribute('role')).toBe('status');
      expect(router.url).toBe('/auth/sign-in');
    });

    it('does not show the message for other values', async () => {
      await setup('/auth/sign-in?registered=yes');

      expect(pageMessage()).toBeNull();
    });

    it('is replaced when the visitor submits', async () => {
      await setup('/auth/sign-in?registered=true');

      await submitForm();

      expect(pageMessage()).toBeNull();
    });
  });

  describe('client validation (FR-02)', () => {
    beforeEach(() => setup());

    it('shows both required messages and sends nothing for an empty form (AC-05)', async () => {
      await submitForm();

      httpTesting.expectNone(SIGN_IN_URL);
      expect(emailError()).toBe('Enter your email address.');
      expect(passwordError()).toBe('Enter your password.');
      expect(document.activeElement).toBe(emailInput());
    });

    it('marks invalid fields with aria-invalid and aria-describedby', async () => {
      await submitForm();

      expect(emailInput().getAttribute('aria-invalid')).toBe('true');
      expect(emailInput().getAttribute('aria-describedby')).toBe('email-error');
      expect(passwordInput().getAttribute('aria-invalid')).toBe('true');
      expect(passwordInput().getAttribute('aria-describedby')).toBe('password-error');
    });

    it('has no aria-invalid or aria-describedby while valid', () => {
      expect(emailInput().hasAttribute('aria-invalid')).toBe(false);
      expect(emailInput().hasAttribute('aria-describedby')).toBe(false);
    });

    it.each([
      ['whitespace only', '   ', 'Enter your email address.'],
      ['no @', 'name.company.com', 'Enter a valid email address, for example name@company.com.'],
      [
        'empty local part',
        '@company.com',
        'Enter a valid email address, for example name@company.com.',
      ],
      ['two @', 'a@b@company.com', 'Enter a valid email address, for example name@company.com.'],
      [
        'no dot in domain',
        'name@company',
        'Enter a valid email address, for example name@company.com.',
      ],
      [
        'inner whitespace',
        'na me@company.com',
        'Enter a valid email address, for example name@company.com.',
      ],
      [
        'longer than 254',
        `${'a'.repeat(243)}@company.com`,
        'Enter a valid email address, for example name@company.com.',
      ],
    ])('rejects an email with %s (AC-06)', async (_case, email, message) => {
      type(emailInput(), email);
      type(passwordInput(), 'secret');
      await submitForm();

      httpTesting.expectNone(SIGN_IN_URL);
      expect(emailError()).toBe(message);
      expect(passwordError()).toBeNull();
    });

    it('rejects a password longer than 128 characters (AC-06)', async () => {
      type(emailInput(), 'sofia@example.com');
      type(passwordInput(), 'x'.repeat(129));
      await submitForm();

      httpTesting.expectNone(SIGN_IN_URL);
      expect(passwordError()).toBe('Use 128 characters or fewer.');
      expect(emailError()).toBeNull();
      expect(document.activeElement).toBe(passwordInput());
    });

    it.each([
      [
        'a 254-character email and a 128-character password',
        `${'a'.repeat(242)}@company.com`,
        'x'.repeat(128),
      ],
      ['a single-space password', 'sofia@example.com', ' '],
      ['an email with surrounding spaces and capitals', '  Sofia@Example.COM ', 'secret'],
    ])('accepts %s and sends it', async (_case, email, password) => {
      const request = await submitValid(email, password);

      expect(request.request.body).toEqual({
        email: email.trim().toLowerCase(),
        password,
        rememberMe: false,
      });
      expect(emailError()).toBeNull();
      expect(passwordError()).toBeNull();
      request.flush(SESSION);
      await stable();
    });

    it('does not validate on blur before the first submit', async () => {
      blur(emailInput());
      await stable();

      expect(emailError()).toBeNull();
    });

    it('does not change a shown message while typing', async () => {
      await submitForm();
      type(emailInput(), 'sofia@example.com');
      await stable();

      expect(emailError()).toBe('Enter your email address.');
    });

    it('clears a corrected field on blur after a failed submit (AC-07)', async () => {
      await submitForm();

      type(emailInput(), 'sofia@example.com');
      blur(emailInput());
      await stable();

      expect(emailError()).toBeNull();
      expect(emailInput().hasAttribute('aria-invalid')).toBe(false);
      expect(passwordError()).toBe('Enter your password.');

      type(passwordInput(), 'secret');
      blur(passwordInput());
      await stable();

      expect(passwordError()).toBeNull();
    });

    it('shows the next failing rule on blur after a failed submit', async () => {
      await submitForm();

      type(emailInput(), 'not-an-email');
      blur(emailInput());
      await stable();

      expect(emailError()).toBe('Enter a valid email address, for example name@company.com.');
    });
  });

  describe('submitting (AC-39)', () => {
    beforeEach(() => setup());

    it('shows the submitting state, locks the inputs and sends one request', async () => {
      const request = await submitValid();

      expect(submitButton().textContent?.trim()).toBe('Signing in…');
      expect(submitButton().disabled).toBe(true);
      expect(submitButton().querySelector('svg[data-p-icon="spinner"]')).not.toBeNull();
      expect(emailInput().readOnly).toBe(true);
      expect(passwordInput().readOnly).toBe(true);
      expect(toggleButton().disabled).toBe(false);

      submitButton().click();
      await submitForm();

      httpTesting.expectNone(SIGN_IN_URL);
      request.flush(SESSION);
      await stable();
    });

    it('sends rememberMe when checked', async () => {
      query<HTMLInputElement>('#remember-me')?.click();
      await stable();

      const request = await submitValid();

      expect(request.request.body).toMatchObject({ rememberMe: true });
      request.flush(SESSION);
      await stable();
    });
  });

  describe('responses', () => {
    beforeEach(() => setup());

    it('stores the session and navigates to Overview on 200 (AC-40)', async () => {
      const request = await submitValid();

      request.flush(SESSION);
      await stable();

      expect(TestBed.inject(SessionService).session()).toEqual(SESSION);
      expect(router.url).toBe('/overview');
      expect(pageMessage()).toBeNull();
    });

    it('shows the 401 message as an alert, keeps the email, clears and focuses the password (AC-41)', async () => {
      await respond(await submitValid(), 401);

      expect(pageMessage()?.textContent).toContain(INVALID_CREDENTIALS);
      expect(pageMessage()?.getAttribute('role')).toBe('alert');
      expect(emailInput().value).toBe('sofia@example.com');
      expect(passwordInput().value).toBe('');
      expect(document.activeElement).toBe(passwordInput());
      expect(submitButton().disabled).toBe(false);
      expect(emailInput().readOnly).toBe(false);
      expect(passwordError()).toBeNull();
    });

    it('keeps the show/hide state after a 401', async () => {
      toggleButton().click();
      await stable();

      await respond(await submitValid(), 401);

      expect(passwordInput().type).toBe('text');
    });

    it('disables submit for Retry-After seconds and shows "2 minutes" for 90 s (AC-42)', async () => {
      const request = await submitValid();
      vi.useFakeTimers();

      request.flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '90' },
      });
      harness.fixture.detectChanges();

      expect(pageMessage()?.textContent).toContain(
        'Too many sign-in attempts. Try again in 2 minutes.',
      );
      expect(pageMessage()?.getAttribute('role')).toBe('alert');
      expect(submitButton().disabled).toBe(true);
      expect(submitButton().textContent?.trim()).toBe('Sign in');
      expect(emailInput().readOnly).toBe(false);
      expect(document.activeElement).toBe(emailInput());
      expect(passwordInput().value).toBe('');

      vi.advanceTimersByTime(89_999);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(true);

      // Enter still fires `submit` on the form; it must be ignored while locked.
      query<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
      harness.fixture.detectChanges();
      httpTesting.expectNone(SIGN_IN_URL);

      vi.advanceTimersByTime(1);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(false);
      expect(pageMessage()?.textContent).toContain('2 minutes');
    });

    it('shows "1 minute" for Retry-After 45 (AC-67)', async () => {
      await respond(await submitValid(), 429, null, { 'Retry-After': '45' });

      expect(pageMessage()?.textContent?.trim()).toBe(
        'Too many sign-in attempts. Try again in 1 minute.',
      );
    });

    it('locks for 60 seconds and shows "1 minute" when Retry-After is absent', async () => {
      const request = await submitValid();
      vi.useFakeTimers();

      request.flush(null, { status: 429, statusText: 'Too Many Requests' });
      harness.fixture.detectChanges();

      expect(pageMessage()?.textContent).toContain('Try again in 1 minute.');
      vi.advanceTimersByTime(59_999);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(true);
      vi.advanceTimersByTime(1);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(false);
    });

    it('shows the generic message and unlocks the form on 500 (AC-43)', async () => {
      await respond(await submitValid(), 500, {
        title: 'Server exploded',
        detail: 'NpgsqlException',
        status: 500,
      });

      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
      expect(host.textContent).not.toContain('Npgsql');
      expect(submitButton().disabled).toBe(false);
      expect(emailInput().readOnly).toBe(false);
      expect(passwordInput().value).toBe('');
      expect(document.activeElement).toBe(submitButton());
    });

    it('shows the generic message and unlocks the form on a network error (AC-43)', async () => {
      const request = await submitValid();

      request.error(new ProgressEvent('error'));
      await stable();

      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
      expect(submitButton().disabled).toBe(false);
      expect(submitButton().textContent?.trim()).toBe('Sign in');
      expect(emailInput().readOnly).toBe(false);
    });

    it.each([413, 415])('shows the generic message on %i', async (status) => {
      await respond(await submitValid(), status, { title: 'x', status });

      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
    });

    it('shows the generic message on a 400 without field keys', async () => {
      await respond(await submitValid(), 400, { title: 'Bad Request', status: 400 });

      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
      expect(emailError()).toBeNull();
    });

    it('shows the generic message on a 400 whose keys are not email or password', async () => {
      await respond(await submitValid(), 400, {
        status: 400,
        errors: { rememberMe: ['Bad value.'] },
      });

      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
      expect(host.textContent).not.toContain('Bad value.');
    });

    it('shows BR-03 server field messages under their fields', async () => {
      await respond(await submitValid(), 400, {
        status: 400,
        errors: { email: ['Enter a valid email address, for example name@company.com.'] },
      });

      expect(emailError()).toBe('Enter a valid email address, for example name@company.com.');
      expect(pageMessage()).toBeNull();
      expect(document.activeElement).toBe(emailInput());
    });

    it('replaces a server field message outside BR-03 with "Enter a valid value." (AC-44)', async () => {
      await respond(await submitValid(), 400, {
        status: 400,
        errors: {
          password: ['The JSON value could not be converted to System.String.'],
          Other: ['Ignored'],
        },
      });

      expect(passwordError()).toBe('Enter a valid value.');
      expect(emailError()).toBeNull();
      expect(host.textContent).not.toContain('System.String');
      expect(document.activeElement).toBe(passwordInput());
    });

    it('replaces the previous error message on a new submit', async () => {
      await respond(await submitValid(), 401);
      expect(pageMessage()).not.toBeNull();

      type(passwordInput(), 'another');
      await submitForm();

      expect(pageMessage()).toBeNull();
      await respond(httpTesting.expectOne(SIGN_IN_URL), 500);
      expect(pageMessage()?.textContent?.trim()).toBe(GENERIC_MESSAGE);
    });
  });

  describe('accessibility (AC-56, axe in jsdom)', () => {
    async function expectNoViolations(): Promise<void> {
      const results = await axe.run(harness.fixture.nativeElement as HTMLElement, AXE_OPTIONS);
      expect(results.passes.length).toBeGreaterThan(0);
      expect(results.violations.map((violation) => violation.id)).toEqual([]);
    }

    it('default state', async () => {
      await setup();
      await expectNoViolations();
    });

    it('validation error state', async () => {
      await setup();
      await submitForm();
      await expectNoViolations();
    });

    it('401 error state', async () => {
      await setup();
      await respond(await submitValid(), 401);
      await expectNoViolations();
    });

    it('registered state', async () => {
      await setup('/auth/sign-in?registered=true');
      await expectNoViolations();
    });
  });
});

/**
 * jsdom has no layout or rendering, so axe cannot compute `color-contrast`;
 * contrast is verified in the browser (Playwright) during the final audit.
 */
const AXE_OPTIONS: axe.RunOptions = {
  rules: { 'color-contrast': { enabled: false } },
};
