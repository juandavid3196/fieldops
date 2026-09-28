import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  provideRouter,
} from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import axe from 'axe-core';

import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { Session } from '../../core/models/session.model';
import { SessionService } from '../../core/services/session.service';
import { companySettingsUnsavedChangesGuard } from '../../features/organizations/guards/company-settings-unsaved-changes.guard';
import type { CompanySetup } from '../../features/organizations/pages/company-setup/company-setup';
import { AppShell, SIGN_OUT_ERROR_MESSAGE } from './app-shell';

const API_BASE_URL = 'http://api.test';
const SESSION_URL = `${API_BASE_URL}/sessions/current`;

const SESSION: Session = {
  user: { id: 'u-1', firstName: ' Alex ', lastName: 'Morgan ', email: 'alex@example.com' },
  organization: { id: 'o-1', name: 'FieldOps Services' },
  role: { code: 'owner', name: 'Owner' },
};

@Component({ template: '<h1>Page</h1>' })
class PageStub {}

@Component({ template: '<p>Sign in stub</p>' })
class SignInStub {}

describe('AppShell', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let sessionService: SessionService;
  let host: HTMLElement;
  const originalMatchMedia = window.matchMedia;

  function stubViewport(desktop: boolean): void {
    window.matchMedia = ((query: string) => ({
      matches: desktop,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
  }

  async function setup(session: Session = SESSION, desktop = true): Promise<void> {
    stubViewport(desktop);
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          {
            path: '',
            component: AppShell,
            children: [
              { path: 'overview', component: PageStub },
              { path: 'admin/company', component: PageStub },
            ],
          },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    sessionService = TestBed.inject(SessionService);
    load(session);

    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/overview');
    await settle();
    host = harness.routeNativeElement as HTMLElement;
  }

  function load(session: Session): void {
    sessionService.loadCurrent().subscribe();
    httpTesting.expectOne({ method: 'GET', url: SESSION_URL }).flush(session);
  }

  const settle = () => harness.fixture.whenStable();
  const linkLabels = () =>
    Array.from(host.querySelectorAll('nav a'), (link) => link.textContent?.trim());
  const userButton = () => host.querySelector<HTMLButtonElement>('.shell__user-button')!;
  const menuItem = () => host.querySelector<HTMLButtonElement>('[role="menuitem"]')!;

  async function openMenuAndSignOut(): Promise<void> {
    userButton().click();
    await settle();
    menuItem().click();
    await settle();
  }

  async function expectAxeClean(): Promise<void> {
    // jsdom has no layout: `color-contrast` is verified in the browser during the final audit.
    const results = await axe.run(document.body, {
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(results.violations.map((violation) => violation.id)).toEqual([]);
  }

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    httpTesting.verify();
  });

  it.each([
    ['owner', true],
    ['viewer', true],
    ['dispatcher', false],
    ['technician', false],
    ['accounting', false],
    ['operations_manager', false],
    ['unknown_role', false],
  ])(
    'shows Company settings by role %s = %s and marks the current page (FR-02, FR-03, BR-01, AC-01, AC-02, AC-06)',
    async (code, canSeeAdministration) => {
      await setup({ ...SESSION, role: { code, name: 'Some role' } });

      expect(linkLabels()).toEqual(
        canSeeAdministration ? ['Overview', 'Company settings'] : ['Overview'],
      );
      expect(host.textContent?.includes('Administration')).toBe(canSeeAdministration);
      expect(host.querySelectorAll('header')).toHaveLength(1);
      expect(host.querySelectorAll('main')).toHaveLength(1);
      expect(host.querySelector('nav')?.getAttribute('aria-label')).toBe('Main navigation');
      expect(host.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
      expect(host.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Overview');

      if (canSeeAdministration) {
        await router.navigateByUrl('/admin/company');
        await settle();
        expect(host.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
        expect(host.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe(
          'Company settings',
        );
      }
      // Desktop: no navigation menu button.
      expect(host.textContent).not.toContain('Open navigation');
    },
  );

  it('shows organization, full name, role and initials, and follows session reloads (FR-04, BR-02, AC-08, AC-09)', async () => {
    await setup();

    expect(host.querySelector('.shell__org')?.textContent?.trim()).toBe('FieldOps Services');
    expect(host.querySelector('.shell__name')?.textContent?.trim()).toBe('Alex Morgan');
    expect(host.querySelector('.shell__role')?.textContent?.trim()).toBe('Owner');
    expect(host.querySelector('.shell__initials')?.textContent?.trim()).toBe('AM');
    expect(host.querySelector('.shell__initials')?.getAttribute('aria-hidden')).toBe('true');
    expect(userButton().getAttribute('aria-label')).toBe('Account menu, Alex Morgan, Owner');

    load({
      user: { ...SESSION.user, firstName: 'alex', lastName: '  ' },
      organization: { id: 'o-1', name: 'Renamed Co' },
      role: { code: 'owner', name: 'Owner' },
    });
    await settle();

    expect(host.querySelector('.shell__org')?.textContent?.trim()).toBe('Renamed Co');
    expect(host.querySelector('.shell__initials')?.textContent?.trim()).toBe('A');
    expect(userButton().getAttribute('aria-label')).toBe('Account menu, alex, Owner');
  });

  it.each([
    [204, 'No Content'],
    [401, 'Unauthorized'],
  ])(
    'clears the session and replaces the route with Sign In on sign-out %s (FR-05, AC-11, AC-12)',
    async (status, statusText) => {
      await setup();
      const replaceSpy = vi.spyOn(router, 'navigateByUrl');

      await openMenuAndSignOut();
      httpTesting
        .expectOne({ method: 'DELETE', url: SESSION_URL })
        .flush(null, { status, statusText });
      await settle();

      expect(sessionService.session()).toBeNull();
      expect(router.url).toBe('/auth/sign-in');
      expect(replaceSpy).toHaveBeenCalledWith('/auth/sign-in', { replaceUrl: true });
    },
  );

  it('sends one request while signing out and, on failure, keeps session and route, alerts and refocuses the menu button (FR-05, AC-13, FR-10)', async () => {
    await setup();

    userButton().click();
    await settle();
    await expectAxeClean();
    menuItem().click();
    menuItem().click();
    await settle();
    expect(menuItem().querySelector('svg[data-p-icon="spinner"]')).not.toBeNull();

    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    const alert = host.querySelector('p-message');
    expect(alert?.textContent?.trim()).toBe(SIGN_OUT_ERROR_MESSAGE);
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(router.url).toBe('/overview');
    expect(sessionService.session()).toEqual(SESSION);
    expect(host.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(userButton());
    await expectAxeClean();
  });

  it('closes the user menu with Escape and returns focus (FR-10)', async () => {
    await setup();

    userButton().click();
    await settle();
    expect(userButton().getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(menuItem());

    menuItem().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(host.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(userButton());
  });

  it('leaves Company settings without a prompt once the session is cleared, and prompts otherwise (FR-09, AC-14)', async () => {
    await setup();
    const canLeave = vi.fn(() => false);
    const run = () =>
      TestBed.runInInjectionContext(() =>
        companySettingsUnsavedChangesGuard(
          { canLeave } as unknown as CompanySetup,
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          {} as RouterStateSnapshot,
        ),
      );

    expect(run()).toBe(false);
    expect(canLeave).toHaveBeenCalledTimes(1);

    await openMenuAndSignOut();
    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(run()).toBe(true);
    expect(canLeave).toHaveBeenCalledTimes(1);
  });

  it('opens a labelled navigation drawer below lg, closes it on selection and returns focus (FR-06, AC-16, AC-17, AC-18)', async () => {
    await setup(SESSION, false);

    expect(host.querySelector('.shell__sidebar')).toBeNull();
    const navButton = host.querySelector<HTMLButtonElement>('.shell__nav-button')!;
    expect(navButton.textContent).toContain('Open navigation');

    navButton.click();
    await settle();

    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(dialog?.getAttribute('aria-labelledby')).toBe('shell-drawer-title');
    expect(dialog?.querySelector('nav')?.getAttribute('aria-label')).toBe('Main navigation');
    expect(dialog?.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Overview');
    expect(dialog?.textContent).toContain('Close navigation');
    await expectAxeClean();

    dialog?.querySelectorAll<HTMLAnchorElement>('nav a')[1].click();
    await settle();

    expect(router.url).toBe('/admin/company');
    expect((harness.routeDebugElement?.componentInstance as AppShell).drawerOpen()).toBe(false);
  });
});
