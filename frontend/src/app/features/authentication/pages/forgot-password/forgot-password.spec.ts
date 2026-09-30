import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ForgotPassword } from './forgot-password';

const API = 'http://api.test';
const REQUEST_URL = `${API}/password-resets`;

describe('ForgotPassword', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let host: HTMLElement;

  const stable = () => harness.fixture.whenStable();
  const q = <T extends Element>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (selector: string) => q(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
  const email = () => q<HTMLInputElement>('#email') as HTMLInputElement;
  const submitButton = () => q<HTMLButtonElement>('button[type="submit"]') as HTMLButtonElement;

  async function submit(value: string): Promise<void> {
    email().value = value;
    email().dispatchEvent(new Event('input'));
    q<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    await stable();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'auth/forgot-password', component: ForgotPassword }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl('/auth/forgot-password', ForgotPassword);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
  });

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  it.each<[string, number, Record<string, string>, () => void | Promise<void>]>([
    [
      '202 shows the neutral confirmation and Use a different email restores the form',
      202,
      {},
      async () => {
        expect(text('h1')).toBe('Check your email');
        expect(text('.invitation__subtitle')).toBe(
          "If an account exists for alex@example.com, we've sent a link to reset your password. It expires in 30 minutes.",
        );
        expect(document.activeElement).toBe(q('h1'));
        q<HTMLButtonElement>('.invitation__switch button')?.click();
        await stable();
        expect(text('h1')).toBe('Reset your password');
        expect(email().value).toBe('Alex@Example.com');
        expect(document.activeElement).toBe(email());
      },
    ],
    [
      '429 locks submit for the Retry-After period',
      429,
      { 'Retry-After': '90' },
      async () => {
        expect(text('p-message')).toBe('Too many attempts. Try again in 2 minutes.');
        expect(submitButton().disabled).toBe(true);
        vi.advanceTimersByTime(90_000);
        harness.fixture.detectChanges();
        expect(submitButton().disabled).toBe(false);
      },
    ],
    [
      '500 shows the generic message without backend text',
      500,
      {},
      () => {
        expect(text('p-message')).toBe(
          "We couldn't send the reset link right now. Try again in a moment.",
        );
        expect(submitButton().disabled).toBe(false);
      },
    ],
  ])('%s (AC-18)', async (_case, status, headers, assertOutcome) => {
    // Invalid values are never sent and show the BR-05 messages.
    await submit('   ');
    expect(text('#email-error')).toBe('Enter your email address.');
    expect(document.activeElement).toBe(email());
    await submit('not-an-email');
    expect(text('#email-error')).toBe('Enter a valid email address, for example name@company.com.');
    httpTesting.expectNone(REQUEST_URL);

    await submit('  Alex@Example.com ');
    const request = httpTesting.expectOne(REQUEST_URL);
    expect(request.request.body).toEqual({ email: 'alex@example.com' });
    expect(submitButton().textContent?.trim()).toBe('Sending…');
    expect(email().readOnly).toBe(true);
    await submit('  Alex@Example.com ');
    httpTesting.expectNone(REQUEST_URL);

    if (status === 429) {
      vi.useFakeTimers();
    }
    request.flush(status === 202 ? null : { status, detail: 'backend text' }, {
      status,
      statusText: 'x',
      headers,
    });
    if (status === 429) {
      harness.fixture.detectChanges();
    } else {
      await stable();
    }
    expect(host.textContent).not.toContain('backend text');
    await assertOutcome();
  });
});
