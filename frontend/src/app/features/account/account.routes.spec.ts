import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { Session } from '../../core/models/session.model';
import { SessionService } from '../../core/services/session.service';
import { createAccountRoutes } from './account.routes';

const API = 'http://api.test';

const SESSION: Session = {
  user: {
    id: 'u-1',
    firstName: 'Ana',
    lastName: 'Lopez',
    email: 'ana@example.com',
    photoUpdatedAt: null,
  },
  organization: { id: 'o-1', name: 'Acme' },
  role: { code: 'technician', name: 'Technician' },
};

const PROFILE = {
  firstName: 'Ana',
  lastName: 'Lopez',
  email: 'ana@example.com',
  phone: '+1 555 123 4567',
  organization: { name: 'Acme' },
  role: { name: 'Technician' },
  photo: null,
};

@Component({ template: '<p>Sign in</p>' })
class SignInStub {}

describe('account routes and layout', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let session: SessionService;

  const stable = () => harness.fixture.whenStable();
  // A guarded navigation waiting on the dialog keeps the app unstable, so wait one macrotask.
  const tick = async (): Promise<void> => {
    await new Promise((resolve) => setTimeout(resolve));
    harness.fixture.detectChanges();
  };
  const host = () => harness.routeNativeElement as HTMLElement;
  const dialogButton = (label: string) =>
    Array.from(document.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')).find(
      (button) => button.textContent?.trim() === label,
    );
  const type = (selector: string, value: string): void => {
    const input = host().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'account', loadChildren: () => import('./account.routes') },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    session = TestBed.inject(SessionService);
    session.setSession(SESSION);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  it('redirects /account to My profile, frames both sections and guards unsaved edits except after sign-out (FR-01, FR-02, FR-12, AC-04, AC-25)', async () => {
    await harness.navigateByUrl('/account');
    httpTesting.expectOne(`${API}/account/profile`).flush(PROFILE);
    await stable();

    expect(router.url).toBe('/account/profile');
    expect(document.title).toBe('My profile · FieldOps');
    expect(host().querySelectorAll('h1')).toHaveLength(1);
    expect(host().querySelector('h1')?.textContent).toBe('Account settings');
    expect(host().textContent).toContain('Manage your profile, preferences and security settings.');
    expect(host().querySelector('.account__breadcrumb')?.textContent).toBe('AccountMy profile');
    const links = () =>
      Array.from(
        host().querySelectorAll<HTMLAnchorElement>('nav[aria-label="Account settings sections"] a'),
      );
    expect(links().map((link) => link.textContent?.trim())).toEqual([
      'My profile',
      'Preferences',
      'Security',
    ]);
    expect(links().map((link) => link.getAttribute('aria-current'))).toEqual(['page', null, null]);

    // Unsaved profile edits: Keep editing stays with the input, Discard changes navigates.
    type('#profile-firstName', 'Anna');
    await stable();
    links()[2].click();
    await tick();
    expect(dialogButton('Keep editing')).toBeDefined();
    dialogButton('Keep editing')!.click();
    await stable();
    expect(router.url).toBe('/account/profile');
    expect(host().querySelector<HTMLInputElement>('#profile-firstName')?.value).toBe('Anna');

    links()[2].click();
    await tick();
    dialogButton('Discard changes')!.click();
    await stable();
    expect(router.url).toBe('/account/security');
    expect(host().querySelector('.account__breadcrumb')?.textContent).toBe('AccountSecurity');
    expect(links().map((link) => link.getAttribute('aria-current'))).toEqual([null, null, 'page']);

    // Preferences sits between the two (FR-05, AC-05); the technician mount lists it too.
    links()[1].click();
    await stable();
    expect(router.url).toBe('/account/preferences');
    expect(document.title).toBe('Preferences · FieldOps');
    expect(host().querySelector('.account__breadcrumb')?.textContent).toBe('AccountPreferences');
    expect(links().map((link) => link.getAttribute('aria-current'))).toEqual([null, 'page', null]);
    expect(
      createAccountRoutes('/today/account', true)[0].children?.map((child) => child.path),
    ).toEqual(['', 'profile', 'preferences', 'security']);
    links()[2].click();
    await stable();
    expect(router.url).toBe('/account/security');

    // Security: any non-empty field prompts; an empty form and a cleared session leave silently.
    type('#security-currentPassword', 'secret');
    await stable();
    links()[0].click();
    await tick();
    expect(dialogButton('Keep editing')).toBeDefined();
    dialogButton('Keep editing')!.click();
    await stable();
    expect(router.url).toBe('/account/security');

    session.signOut().subscribe();
    httpTesting.expectOne({ method: 'DELETE', url: `${API}/sessions/current` }).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    await router.navigateByUrl('/auth/sign-in');
    await stable();
    expect(router.url).toBe('/auth/sign-in');
    expect(dialogButton('Keep editing')).toBeUndefined();
  }, 30_000);
});
