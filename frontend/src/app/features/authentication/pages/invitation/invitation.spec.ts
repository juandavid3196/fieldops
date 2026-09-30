import { Location } from '@angular/common';
import { HttpRequest, provideHttpClient, withInterceptors } from '@angular/common/http';
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
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { InvitationDetails } from '../../models/invitation.model';
import { Invitation } from './invitation';

const API = 'http://api.test';
const VALIDATE_URL = `${API}/invitations/validate`;
const ACCEPT_URL = `${API}/invitations/accept`;
const ACCEPT_EXISTING_URL = `${API}/invitations/accept-existing`;
const SESSIONS_URL = `${API}/sessions`;
const STORAGE_KEY = 'fieldops.invitation-token';
const TOKEN = 'Abc_123-'.repeat(5) + 'xyz';
const PASSWORD = 'a long enough password';
const GENERIC = "We couldn't accept the invitation right now. Try again in a moment.";

const DETAIL: InvitationDetails = {
  organizationName: 'Acme Services',
  inviterName: 'Alex Morgan',
  email: 'olivia.carter@company.com',
  firstName: 'Olivia',
  lastName: 'Carter',
  role: { code: 'dispatcher', name: 'Dispatcher' },
  isAllBranches: false,
  branches: [{ name: 'Austin Central' }, { name: 'North Austin' }],
  expiresAt: new Date(Date.now() + 6.5 * 86_400_000).toISOString(),
};

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Olivia', lastName: 'Carter', email: DETAIL.email },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'dispatcher', name: 'Dispatcher' },
};

@Component({ template: '<p>Overview stub</p>' })
class OverviewStub {}

describe('Invitation', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let host: HTMLElement;
  let urlAtRequest: string[];

  async function setup(url: string): Promise<void> {
    urlAtRequest = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'auth/invitation', component: Invitation },
          { path: 'auth/sign-in', component: OverviewStub },
          { path: 'overview', component: OverviewStub },
        ]),
        provideHttpClient(
          withInterceptors([
            (request: HttpRequest<unknown>, next) => {
              urlAtRequest.push(TestBed.inject(Location).path(true));
              return next(request);
            },
            authInterceptor,
            errorInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl(url, Invitation);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
  }

  /** Opens the page with a stored token and answers validate with the given detail. */
  async function setupReady(detail: InvitationDetails = DETAIL): Promise<void> {
    await setup(`/auth/invitation#token=${TOKEN}`);
    httpTesting.expectOne(VALIDATE_URL).flush(detail);
    await stable();
  }

  beforeEach(() => sessionStorage.clear());

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
    sessionStorage.clear();
  });

  const stable = () => harness.fixture.whenStable();
  const q = <T extends Element>(selector: string): T | null => host.querySelector<T>(selector);
  const input = (id: string) => q<HTMLInputElement>(`#${id}`) as HTMLInputElement;
  const text = (selector: string) => q(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
  const submitButton = () => q<HTMLButtonElement>('button[type="submit"]') as HTMLButtonElement;
  const message = () => text('p-message');
  const switchButton = () =>
    q<HTMLButtonElement>('.invitation__switch button') as HTMLButtonElement;

  function type(id: string, value: string): void {
    input(id).value = value;
    input(id).dispatchEvent(new Event('input'));
  }

  async function fillNewAccount(): Promise<void> {
    type('password', PASSWORD);
    type('confirm-password', PASSWORD);
    await stable();
  }

  async function submitForm(): Promise<void> {
    q<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    await stable();
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

  it.each([
    ['a fragment token', `/auth/invitation#token=${TOKEN}`, TOKEN, true],
    ['a query token', `/auth/invitation?token=${TOKEN}`, null, false],
    ['no token', '/auth/invitation', null, false],
  ])('captures %s and replaces the URL first (AC-16)', async (_case, url, stored, requests) => {
    await setup(url);
    const location = TestBed.inject(Location);

    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(stored);
    expect(location.path(true)).toBe('/auth/invitation');
    if (requests) {
      const request = httpTesting.expectOne(VALIDATE_URL);
      expect(request.request.body).toEqual({ token: TOKEN });
      expect(request.request.url).not.toContain(TOKEN);
      expect(urlAtRequest).toEqual(['/auth/invitation']);
      request.flush(DETAIL);
      await stable();
    } else {
      httpTesting.expectNone(VALIDATE_URL);
      expect(text('h1')).toBe('This invitation is no longer available');
      expect(host.textContent).not.toContain('Olivia');
    }
  });

  it.each([
    ['410', 410, { status: 410 }],
    [
      '400 with only the token key',
      400,
      { status: 400, errors: { token: ['Enter a valid value.'] } },
    ],
  ])(
    'shows the unavailable screen and clears the token on %s (AC-17)',
    async (_case, status, body) => {
      await setup(`/auth/invitation#token=${TOKEN}`);
      expect(q('p-skeleton')).not.toBeNull();
      expect(q('dl')).toBeNull();

      await respond(httpTesting.expectOne(VALIDATE_URL), status, body);

      expect(text('h1')).toBe('This invitation is no longer available');
      expect(host.textContent).toContain(
        'This invitation can no longer be used. Ask an Owner at your organization to send you a new one.',
      );
      expect(host.textContent).toContain(
        'For your security, expired and replaced invitation links',
      );
      expect(host.textContent).toContain('Need help? Contact your organization.');
      expect(q('dl')).toBeNull();
      expect(host.querySelectorAll('h1')).toHaveLength(1);
      expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      expect(document.activeElement).toBe(q('h1'));
    },
  );

  it('locks Retry after a validate 429, retries with the stored token and shows empty branches (AC-17)', async () => {
    await setup(`/auth/invitation#token=${TOKEN}`);
    const retry = () => q<HTMLButtonElement>('button.invitation__submit') as HTMLButtonElement;
    vi.useFakeTimers();

    httpTesting
      .expectOne(VALIDATE_URL)
      .flush(null, { status: 429, statusText: 'x', headers: { 'Retry-After': '90' } });
    harness.fixture.detectChanges();

    expect(message()).toBe('Too many attempts. Try again in 2 minutes.');
    expect(retry().disabled).toBe(true);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    vi.advanceTimersByTime(89_999);
    harness.fixture.detectChanges();
    expect(retry().disabled).toBe(true);
    vi.advanceTimersByTime(1);
    harness.fixture.detectChanges();
    expect(retry().disabled).toBe(false);

    retry().click();
    httpTesting.expectOne(VALIDATE_URL).flush(null, { status: 500, statusText: 'x' });
    harness.fixture.detectChanges();
    expect(message()).toBe(
      "We couldn't load this invitation. Check your connection and try again.",
    );
    expect(retry().disabled).toBe(false);

    retry().click();
    const request = httpTesting.expectOne(VALIDATE_URL);
    expect(request.request.body).toEqual({ token: TOKEN });
    request.flush({ ...DETAIL, isAllBranches: false, branches: [] });
    harness.fixture.detectChanges();
    expect(text('dl')).toContain('Branch accessNo active branches');
  });

  it('follows the new-account rules, shows passwords and navigates on success (AC-18)', async () => {
    await setupReady();
    const router = TestBed.inject(Router);

    expect(text('h1')).toBe('Join Acme Services');
    expect(text('.invitation__subtitle')).toBe(
      'Alex Morgan invited you to join their FieldOps workspace.',
    );
    expect(text('dl')).toContain('Emailolivia.carter@company.com');
    expect(text('dl')).toContain('RoleDispatcher');
    expect(text('dl')).toContain('Branch accessAustin Central, North Austin');
    expect(input('first-name').value).toBe('Olivia');
    expect(input('last-name').value).toBe('Carter');
    expect(input('password').getAttribute('autocomplete')).toBe('new-password');
    expect(text('.invitation__lock')).toBe(
      'This invitation can only be used once and expires in 7 days.',
    );
    expect(text('#password-requirements')).toContain('12–128 characters (not met)');
    expect(document.activeElement).toBe(q('h1'));

    // Invalid submit: nothing is sent, messages follow BR-06, focus goes to the first invalid field.
    type('first-name', '   ');
    type('password', 'short');
    type('confirm-password', 'other');
    await submitForm();
    httpTesting.expectNone(ACCEPT_URL);
    expect(text('#first-name-error')).toBe('Enter a first name.');
    expect(text('#password-error')).toBe('Use 12 to 128 characters.');
    expect(text('#confirm-password-error')).toBe("Passwords don't match.");
    expect(input('first-name').getAttribute('aria-invalid')).toBe('true');
    expect(input('first-name').getAttribute('aria-describedby')).toBe('first-name-error');
    expect(document.activeElement).toBe(input('first-name'));

    type('password', DETAIL.email.toUpperCase());
    await submitForm();
    expect(text('#password-error')).toBe('Choose a password that is different from your email.');

    // Show password controls both fields.
    q<HTMLLabelElement>('label[for="show-password"]')?.click();
    await stable();
    expect(input('password').type).toBe('text');
    expect(input('confirm-password').type).toBe('text');

    type('first-name', '  Olivia ');
    await fillNewAccount();
    await submitForm();
    const request = httpTesting.expectOne(ACCEPT_URL);
    expect(request.request.body).toEqual({
      token: TOKEN,
      firstName: 'Olivia',
      lastName: 'Carter',
      password: PASSWORD,
    });
    expect(submitButton().textContent?.trim()).toBe('Accepting…');
    expect(submitButton().disabled).toBe(true);
    expect(input('first-name').readOnly).toBe(true);

    await submitForm();
    httpTesting.expectNone(ACCEPT_URL);

    request.flush(SESSION);
    await stable();
    expect(TestBed.inject(SessionService).session()).toEqual(SESSION);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(router.url).toBe('/overview');
  });

  it.each<[string, number, object | null, boolean, () => void]>([
    [
      'field keys with an unknown message',
      400,
      {
        status: 400,
        errors: { firstName: ['Enter a first name.'], password: ['Weird server text'] },
      },
      false,
      () => {
        expect(text('#first-name-error')).toBe('Enter a first name.');
        expect(text('#password-error')).toBe('Enter a valid value.');
        expect(message()).toBeNull();
      },
    ],
    [
      'an unknown field key',
      400,
      { status: 400, errors: { other: ['x'] } },
      false,
      () => expect(message()).toBe(GENERIC),
    ],
    [
      'no keys',
      400,
      { status: 400, title: 'Bad Request' },
      false,
      () => expect(message()).toBe(GENERIC),
    ],
    [
      'only the token key',
      400,
      { status: 400, errors: { token: ['Enter a valid value.'] } },
      false,
      () => {
        expect(text('h1')).toBe('This invitation is no longer available');
        expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      },
    ],
    [
      '409 account_exists',
      409,
      { status: 409, code: 'account_exists', detail: 'backend text' },
      false,
      () => {
        expect(message()).toBe('An account already exists for this email. Sign in to accept.');
        expect(q('p-message + button')?.textContent?.trim()).toBe('Sign in to accept');
        expect(host.textContent).not.toContain('backend text');
      },
    ],
    [
      '409 membership_exists',
      409,
      { status: 409, code: 'membership_exists' },
      false,
      () =>
        expect(message()).toBe(
          "This invitation can't be accepted with this account. Contact an Owner at Acme Services.",
        ),
    ],
    [
      '410',
      410,
      { status: 410, title: 'Gone' },
      false,
      () => expect(text('h1')).toBe('This invitation is no longer available'),
    ],
    [
      '429 with Retry-After 90',
      429,
      null,
      true,
      () => {
        expect(message()).toBe('Too many attempts. Try again in 2 minutes.');
        expect(submitButton().disabled).toBe(true);
      },
    ],
    [
      '500',
      500,
      { status: 500, detail: 'NpgsqlException' },
      false,
      () => {
        expect(message()).toBe(GENERIC);
        expect(host.textContent).not.toContain('Npgsql');
      },
    ],
    ['a network error', 0, null, false, () => expect(message()).toBe(GENERIC)],
  ])(
    'maps accept response %s to BR-17 copy (AC-19)',
    async (_case, status, body, retryAfter, check) => {
      await setupReady();
      await fillNewAccount();
      await submitForm();
      const request = httpTesting.expectOne(ACCEPT_URL);

      if (status === 0) {
        request.error(new ProgressEvent('error'));
      } else {
        request.flush(body, {
          status,
          statusText: 'Error',
          headers: retryAfter ? { 'Retry-After': '90' } : {},
        });
      }
      await stable();

      check();
      if (q('form') !== null) {
        // Input and token are kept unless the invitation became unavailable.
        expect(input('first-name').value).toBe('Olivia');
        expect(input('password').value).toBe(PASSWORD);
        expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
      }
    },
  );

  it('signs in with the invited email, then accepts as the existing user (AC-20)', async () => {
    await setupReady();
    const router = TestBed.inject(Router);

    switchButton().click();
    await stable();
    expect(text('h1')).toBe('Join Acme Services');
    expect(document.activeElement).toBe(q('h1'));
    expect(input('email').readOnly).toBe(true);
    expect(input('email').value).toBe(DETAIL.email);
    expect(input('password').getAttribute('autocomplete')).toBe('current-password');
    expect(q('#confirm-password')).toBeNull();
    expect(submitButton().textContent?.trim()).toBe('Sign in and accept');
    expect(switchButton().textContent?.trim()).toBe('Create your account');

    await submitForm();
    httpTesting.expectNone(SESSIONS_URL);
    expect(text('#password-error')).toBe('Enter your password.');

    type('password', 'wrong password');
    await submitForm();
    const first = httpTesting.expectOne(SESSIONS_URL);
    expect(first.request.body).toEqual({
      email: DETAIL.email,
      password: 'wrong password',
      rememberMe: false,
    });
    expect(submitButton().textContent?.trim()).toBe('Signing in…');
    await respond(first, 401);
    httpTesting.expectNone(ACCEPT_EXISTING_URL);
    expect(message()).toBe('The email or password is incorrect. Check your details and try again.');
    expect(input('password').value).toBe('');
    expect(document.activeElement).toBe(input('password'));
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);

    type('password', PASSWORD);
    await submitForm();
    httpTesting.expectOne(SESSIONS_URL).flush(SESSION);
    await stable();
    const accept = httpTesting.expectOne(ACCEPT_EXISTING_URL);
    expect(accept.request.body).toEqual({ token: TOKEN });
    accept.flush(SESSION);
    await stable();

    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(router.url).toBe('/overview');
  });
});
