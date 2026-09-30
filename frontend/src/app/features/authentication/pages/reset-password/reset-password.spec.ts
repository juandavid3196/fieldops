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
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ResetPassword } from './reset-password';

const API = 'http://api.test';
const VALIDATE_URL = `${API}/password-resets/validate`;
const CONFIRM_URL = `${API}/password-resets/confirm`;
const STORAGE_KEY = 'fieldops.password-reset-token';
const TOKEN = 'Abc_123-'.repeat(5) + 'xyz';
const EMAIL = 'olivia.carter@company.com';
const PASSWORD = 'a long enough password';
const GENERIC = "We couldn't reset your password right now. Try again in a moment.";
const UNAVAILABLE = 'This reset link is no longer available';

@Component({ template: '<p>Stub</p>' })
class RouteStub {}

describe('ResetPassword', () => {
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
          { path: 'auth/reset-password', component: ResetPassword },
          { path: 'auth/forgot-password', component: RouteStub },
          { path: 'auth/sign-in', component: RouteStub },
        ]),
        provideHttpClient(
          withInterceptors([
            (request: HttpRequest<unknown>, next) => {
              urlAtRequest.push(TestBed.inject(Location).path(true));
              return next(request);
            },
            errorInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl(url, ResetPassword);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
  }

  async function setupReady(): Promise<void> {
    await setup(`/auth/reset-password#token=${TOKEN}`);
    httpTesting.expectOne(VALIDATE_URL).flush({ email: EMAIL });
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
  const retryButton = () => q<HTMLButtonElement>('button.invitation__submit') as HTMLButtonElement;

  function type(id: string, value: string): void {
    input(id).value = value;
    input(id).dispatchEvent(new Event('input'));
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
    ['a fragment token', `/auth/reset-password#token=${TOKEN}`, TOKEN, true],
    ['a query token', `/auth/reset-password?token=${TOKEN}`, null, false],
    ['no token', '/auth/reset-password', null, false],
  ])('captures %s and replaces the URL first (AC-19)', async (_case, url, stored, requests) => {
    await setup(url);

    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(stored);
    expect(TestBed.inject(Location).path(true)).toBe('/auth/reset-password');
    if (requests) {
      const request = httpTesting.expectOne(VALIDATE_URL);
      expect(request.request.body).toEqual({ token: TOKEN });
      expect(request.request.url).not.toContain(TOKEN);
      expect(urlAtRequest).toEqual(['/auth/reset-password']);
      request.flush({ email: EMAIL });
      await stable();
    } else {
      httpTesting.expectNone(VALIDATE_URL);
      expect(text('h1')).toBe(UNAVAILABLE);
    }
  });

  it('shows the validate states, clears the token when unavailable and locks Retry on 429 (AC-19, AC-20)', async () => {
    // Pending, then 410 and token-only 400 lead to the unavailable state.
    for (const [status, body] of [
      [410, { status: 410 }],
      [400, { status: 400, errors: { token: ['Enter a valid value.'] } }],
    ] as const) {
      sessionStorage.clear();
      await setup(`/auth/reset-password#token=${TOKEN}`);
      expect(q('p-skeleton')).not.toBeNull();
      expect(host.textContent).not.toContain(EMAIL);
      await respond(httpTesting.expectOne(VALIDATE_URL), status, body);
      expect(text('h1')).toBe(UNAVAILABLE);
      expect(text('.invitation__subtitle')).toBe('Request a new one.');
      expect(q<HTMLAnchorElement>('a.invitation__submit')?.getAttribute('href')).toBe(
        '/auth/forgot-password',
      );
      expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      expect(document.activeElement).toBe(q('h1'));
      TestBed.resetTestingModule();
    }

    await setup(`/auth/reset-password#token=${TOKEN}`);
    vi.useFakeTimers();
    httpTesting
      .expectOne(VALIDATE_URL)
      .flush(null, { status: 429, statusText: 'x', headers: { 'Retry-After': '90' } });
    harness.fixture.detectChanges();
    expect(text('p-message')).toBe('Too many attempts. Try again in 2 minutes.');
    expect(retryButton().disabled).toBe(true);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    vi.advanceTimersByTime(90_000);
    harness.fixture.detectChanges();
    expect(retryButton().disabled).toBe(false);

    retryButton().click();
    httpTesting.expectOne(VALIDATE_URL).flush(null, { status: 500, statusText: 'x' });
    harness.fixture.detectChanges();
    expect(text('p-message')).toBe(
      "We couldn't load this reset link. Check your connection and try again.",
    );
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);

    retryButton().click();
    const request = httpTesting.expectOne(VALIDATE_URL);
    expect(request.request.body).toEqual({ token: TOKEN });
    request.flush({ email: EMAIL });
    harness.fixture.detectChanges();
    expect(text('h1')).toBe('Create a new password');
  });

  it('enforces the password rules, reveals both fields and completes the reset (AC-21)', async () => {
    await setupReady();
    const router = TestBed.inject(Router);

    expect(text('h1')).toBe('Create a new password');
    expect(text('.invitation__subtitle')).toBe(`Choose a new password for ${EMAIL}.`);
    expect(input('password').getAttribute('autocomplete')).toBe('new-password');
    expect(text('#password-requirements')).toContain('12–128 characters (not met)');
    expect(document.activeElement).toBe(q('h1'));

    type('password', 'short');
    await stable();
    type('confirm-password', 'other');
    await submitForm();
    httpTesting.expectNone(CONFIRM_URL);
    expect(text('#password-error')).toBe('Use 12 to 128 characters.');
    expect(text('#confirm-password-error')).toBe("Passwords don't match.");
    expect(input('password').getAttribute('aria-describedby')).toBe('password-error');
    expect(document.activeElement).toBe(input('password'));

    type('password', EMAIL.toUpperCase());
    await submitForm();
    expect(text('#password-error')).toBe('Choose a password that is different from your email.');
    expect(text('#password-requirements')).toContain('Must not match your email (not met)');

    q<HTMLLabelElement>('label[for="show-password"]')?.click();
    await stable();
    expect(input('password').type).toBe('text');
    expect(input('confirm-password').type).toBe('text');

    type('password', PASSWORD);
    type('confirm-password', PASSWORD);
    await submitForm();
    const request = httpTesting.expectOne(CONFIRM_URL);
    expect(request.request.body).toEqual({ token: TOKEN, password: PASSWORD });
    expect(submitButton().textContent?.trim()).toBe('Resetting…');
    expect(input('password').readOnly).toBe(true);
    await submitForm();
    httpTesting.expectNone(CONFIRM_URL);

    request.flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(text('h1')).toBe('Password updated');
    expect(host.textContent).toContain(
      'For your protection, your other active sessions have been signed out.',
    );
    expect(host.textContent).not.toContain(EMAIL);

    q<HTMLButtonElement>('button.invitation__submit')?.click();
    await stable();
    expect(router.url).toBe('/auth/sign-in');
  });

  it.each<[string, number, object | null, Record<string, string>, string | null, string | null]>([
    ['410 (unavailable)', 410, { status: 410 }, {}, null, UNAVAILABLE],
    [
      '400 with only the token key (unavailable)',
      400,
      { status: 400, errors: { token: ['Enter a valid value.'] } },
      {},
      null,
      UNAVAILABLE,
    ],
    [
      'password field message',
      400,
      { status: 400, errors: { password: ['Use 12 to 128 characters.'] } },
      {},
      'Use 12 to 128 characters.',
      null,
    ],
    [
      'unknown server message',
      400,
      { status: 400, errors: { password: ['Weird server text'] } },
      {},
      'Enter a valid value.',
      null,
    ],
    [
      'unknown key',
      400,
      { status: 400, errors: { other: ['Weird server text'] } },
      {},
      'Enter a valid value.',
      null,
    ],
    ['no keys', 400, { status: 400 }, {}, null, GENERIC],
    ['413', 413, null, {}, null, GENERIC],
    ['500', 500, { status: 500, detail: 'Weird server text' }, {}, null, GENERIC],
    ['network error', 0, null, {}, null, GENERIC],
    ['429', 429, null, { 'Retry-After': '90' }, null, 'Too many attempts. Try again in 2 minutes.'],
  ])('maps the confirm response: %s (AC-22)', async (_case, status, body, headers, field, page) => {
    await setupReady();
    type('password', PASSWORD);
    type('confirm-password', PASSWORD);
    await submitForm();
    const request = httpTesting.expectOne(CONFIRM_URL);
    if (status === 429) {
      vi.useFakeTimers();
    }
    if (status === 0) {
      request.error(new ProgressEvent('error'));
    } else {
      request.flush(body, { status, statusText: 'Error', headers });
    }
    harness.fixture.detectChanges();
    if (page === UNAVAILABLE) {
      expect(text('h1')).toBe(UNAVAILABLE);
      expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      expect(host.textContent).not.toContain(EMAIL);
      return;
    }

    expect(text('#password-error')).toBe(field);
    expect(text('p-message')).toBe(page);
    expect(host.textContent).not.toContain('Weird server text');
    expect(input('password').value).toBe(PASSWORD);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    if (status === 429) {
      expect(submitButton().disabled).toBe(true);
      vi.advanceTimersByTime(89_999);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(true);
      vi.advanceTimersByTime(1);
      harness.fixture.detectChanges();
      expect(submitButton().disabled).toBe(false);
    }
  });
});
