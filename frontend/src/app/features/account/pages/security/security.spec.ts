import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { Security } from './security';

const API = 'http://api.test';
const PASSWORD_URL = `${API}/account/password`;

const SESSION: Session = {
  user: {
    id: 'u-1',
    firstName: 'Ana',
    lastName: 'Lopez',
    email: 'Ana@Example.com',
    photoUpdatedAt: null,
  },
  organization: { id: 'o-1', name: 'Acme' },
  role: { code: 'owner', name: 'Owner' },
};

const VALID_NEW = 'a-much-longer-secret';

describe('Security', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  const host = () => harness.routeNativeElement as HTMLElement;
  const stable = () => harness.fixture.whenStable();
  const field = (key: string) => host().querySelector<HTMLInputElement>(`#security-${key}`)!;
  const button = (label: string) =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    )!;
  const text = () => host().textContent?.replace(/\s+/g, ' ') ?? '';
  const error = (key: string) =>
    host().querySelector(`#security-${key}-error`)?.textContent?.trim();
  const type = (key: string, value: string): void => {
    field(key).value = value;
    field(key).dispatchEvent(new Event('input'));
  };
  const requirement = (index: number) => host().querySelectorAll('.security__requirement')[index];
  const fill = (current: string, next: string, confirm: string): void => {
    type('currentPassword', current);
    type('newPassword', next);
    type('confirmPassword', confirm);
  };

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: '', component: Security }]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession(SESSION);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', Security);
  });

  afterEach(() => {
    httpTesting.verify();
    vi.useRealTimers();
  });

  it('updates the requirements live, toggles visibility, blocks invalid submits and clears with Cancel (FR-10, AC-11, AC-17, AC-18)', async () => {
    const met = () =>
      [0, 1, 2].map((index) => requirement(index).classList.contains('security__requirement--met'));
    expect(met()).toEqual([false, false, false]);

    // 11 characters, 12 characters, the email in another case, the current password.
    for (const [current, next, expected] of [
      ['old-secret', 'short-11-chr', [true, true, true]],
      ['old-secret', 'short-11-ch', [false, true, true]],
      ['old-secret', 'ana@EXAMPLE.com', [true, false, true]],
      ['same-password-12', 'same-password-12', [true, true, false]],
    ] as const) {
      type('currentPassword', current);
      type('newPassword', next);
      await stable();
      expect(met()).toEqual([...expected]);
    }
    expect(requirement(0).textContent).toMatch(/12–128 characters\s*,\s*(met|not met)/);
    expect(host().querySelector('.security__requirements')?.getAttribute('aria-live')).toBe(
      'polite',
    );

    const toggle = host().querySelector<HTMLButtonElement>('[aria-label="Show new password"]')!;
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    toggle.click();
    await stable();
    expect(field('newPassword').type).toBe('text');
    expect(
      host().querySelector('[aria-label="Hide new password"]')?.getAttribute('aria-pressed'),
    ).toBe('true');
    expect(host().querySelector('[aria-label="Show current password"]')).not.toBeNull();
    expect(host().querySelector('[aria-label="Show confirmation password"]')).not.toBeNull();

    // Client validation: no request for empty, short, email, current and mismatching values.
    for (const [current, next, confirm, expected] of [
      [
        '',
        '',
        '',
        {
          currentPassword: 'This field is required.',
          newPassword: 'This field is required.',
          confirmPassword: 'This field is required.',
        },
      ],
      ['old', 'short', 'short', { newPassword: 'Use 12 to 128 characters.' }],
      [
        'old',
        'ana@example.com',
        'ana@example.com',
        { newPassword: 'Choose a password that is different from your email.' },
      ],
      [
        'same-password-12',
        'same-password-12',
        'same-password-12',
        { newPassword: 'Choose a password that is different from your current password.' },
      ],
      ['old', VALID_NEW, 'other', { confirmPassword: "Passwords don't match." }],
    ] as const) {
      fill(current, next, confirm);
      button('Update password').click();
      await stable();
      for (const key of ['currentPassword', 'newPassword', 'confirmPassword']) {
        expect(error(key)).toBe((expected as Record<string, string>)[key]);
      }
    }
    httpTesting.expectNone(PASSWORD_URL);

    button('Cancel').click();
    await stable();
    expect([
      field('currentPassword').value,
      field('newPassword').value,
      field('confirmPassword').value,
    ]).toEqual(['', '', '']);
    expect(host().querySelector('.security__error')).toBeNull();
  });

  it('maps responses: field messages, generic failures, one request in flight, success, and the 429 timer (FR-11, AC-20 to AC-23)', async () => {
    const submit = async (): Promise<void> => {
      fill('old-password', VALID_NEW, VALID_NEW);
      button('Update password').click();
      await stable();
    };

    await submit();
    httpTesting
      .expectOne({ method: 'POST', url: PASSWORD_URL })
      .flush(
        { errors: { currentPassword: ['Your current password is incorrect.'] } },
        { status: 400, statusText: 'Bad' },
      );
    await stable();
    expect(error('currentPassword')).toBe('Your current password is incorrect.');
    expect(field('currentPassword').value).toBe('old-password');

    for (const failure of [
      { status: 500, statusText: 'x' },
      { status: 0, statusText: '' },
    ]) {
      await submit();
      const request = httpTesting.expectOne({ method: 'POST', url: PASSWORD_URL });
      expect(button('Updating…')).toBeDefined();
      button('Updating…').click();
      if (failure.status === 0) {
        request.error(new ProgressEvent('error'));
      } else {
        request.flush(null, failure);
      }
      await stable();
      expect(text()).toContain("We couldn't update your password. Try again.");
      expect(field('newPassword').value).toBe(VALID_NEW);
    }

    // 429: message with rounded-up minutes, submit disabled until Retry-After ends.
    await submit();
    httpTesting
      .expectOne({ method: 'POST', url: PASSWORD_URL })
      .flush(null, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '61' } });
    await stable();
    expect(text()).toContain('Too many attempts. Try again in 2 minutes.');
    expect(button('Update password').disabled).toBe(true);
    await vi.advanceTimersByTimeAsync(61_000);
    await stable();
    expect(text()).not.toContain('Too many attempts');
    expect(button('Update password').disabled).toBe(false);

    await submit();
    httpTesting
      .expectOne({ method: 'POST', url: PASSWORD_URL })
      .flush(null, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '30' } });
    await stable();
    expect(text()).toContain('Too many attempts. Try again in 1 minute.');
    await vi.advanceTimersByTimeAsync(30_000);
    await stable();

    // Success: body has no confirmation, fields are cleared, toast is shown.
    await submit();
    const ok = httpTesting.expectOne({ method: 'POST', url: PASSWORD_URL });
    expect(ok.request.body).toEqual({ currentPassword: 'old-password', newPassword: VALID_NEW });
    ok.flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    expect(text()).toContain('Password updated');
    expect([
      field('currentPassword').value,
      field('newPassword').value,
      field('confirmPassword').value,
    ]).toEqual(['', '', '']);
    expect(harness.routeDebugElement!.componentInstance.canLeave()).toBe(true);
  });
});
