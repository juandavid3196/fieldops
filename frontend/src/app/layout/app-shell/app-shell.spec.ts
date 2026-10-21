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
const SUMMARY_URL = `${API_BASE_URL}/notifications/summary`;
const MESSAGES_SUMMARY_URL = `${API_BASE_URL}/messages/summary`;
const MESSAGES_ROLES = ['owner', 'operations_manager', 'dispatcher'];

const SESSION: Session = {
  user: {
    id: 'u-1',
    firstName: ' Alex ',
    lastName: 'Morgan ',
    email: 'alex@example.com',
    photoUpdatedAt: null,
  },
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

  type Mode = 'drawer' | 'rail' | 'full';

  /** `md` (48rem) matches from rail up; `admin` (68.75rem) only in full mode. */
  function stubViewport(mode: Mode): void {
    window.matchMedia = ((query: string) => ({
      matches: query.includes('48rem') ? mode !== 'drawer' : mode === 'full',
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
  }

  async function setup(
    session: Session = SESSION,
    mode: Mode = 'full',
    unread = 0,
    messages = 0,
  ): Promise<void> {
    stubViewport(mode);
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          {
            path: '',
            component: AppShell,
            children: [
              { path: 'overview', component: PageStub },
              { path: 'dashboard', component: PageStub },
              { path: 'admin/company', component: PageStub },
              { path: 'admin/products-services', component: PageStub },
              { path: 'coming-soon/:module', component: PageStub },
              { path: 'jobs', component: PageStub },
              { path: 'jobs/:id', component: PageStub },
              { path: 'notifications', component: PageStub },
              { path: 'messages', component: PageStub },
              { path: 'messages/:conversationId', component: PageStub },
              {
                path: 'account',
                children: [
                  { path: '', pathMatch: 'full', redirectTo: 'profile' },
                  { path: 'profile', component: PageStub },
                ],
              },
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
    await harness.navigateByUrl(
      MANAGE_ROLES.includes(session.role.code) ? '/dashboard' : '/overview',
    );
    await settle();
    // notifications-center BR-16: internal roles load the bell count once, the technician never does.
    if (session.role.code !== 'technician') {
      httpTesting.expectOne({ method: 'GET', url: SUMMARY_URL }).flush({ unread, today: 0 });
      await settle();
    }
    // technician-messages BR-14: only Owner, Operations Manager and Dispatcher poll Messages.
    if (MESSAGES_ROLES.includes(session.role.code)) {
      httpTesting
        .expectOne({ method: 'GET', url: MESSAGES_SUMMARY_URL })
        .flush({ unreadMessages: messages, unreadConversations: 1 });
      await settle();
    }
    host = harness.routeNativeElement as HTMLElement;
  }

  function load(session: Session): void {
    sessionService.loadCurrent().subscribe();
    httpTesting.expectOne({ method: 'GET', url: SESSION_URL }).flush(session);
  }

  const settle = () => harness.fixture.whenStable();
  const linkLabels = () =>
    Array.from(host.querySelectorAll('nav a, nav .sidebar__link--parent'), (link) =>
      link.textContent?.trim(),
    );
  const userButton = () => host.querySelector<HTMLButtonElement>('.shell__user-button')!;
  const orgButton = () => host.querySelector<HTMLButtonElement>('.shell__org-button');
  const menuItem = () => host.querySelector<HTMLButtonElement>('[role="menuitem"]')!;
  const accountItems = () =>
    Array.from(host.querySelectorAll<HTMLElement>('[aria-label="Account"] [role="menuitem"]'));
  const signOutItem = () => accountItems().at(-1)!;
  // operations-dashboard BR-01: Manage roles see Dashboard where other roles see Overview.
  const MANAGE_ROLES = ['owner', 'operations_manager', 'dispatcher'];
  const MODULE_LABELS = [
    'Overview',
    'Requests',
    'Quotes',
    'Jobs',
    'Schedule',
    'Customers',
    'Team',
    'Products and services',
    'Invoices',
  ];

  async function openMenuAndSignOut(): Promise<void> {
    userButton().click();
    await settle();
    signOutItem().click();
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
    'renders the BR-01 navigation for role %s (Administration = %s) and marks the current page (FR-01, AC-01, AC-02, AC-03)',
    async (code, canSeeAdministration) => {
      await setup({ ...SESSION, role: { code, name: 'Some role' } });

      const first = MANAGE_ROLES.includes(code) ? 'Dashboard' : 'Overview';
      // The sidebar offers only the pages the role can open (each page's own read rules).
      // reports-workspace BR-23/AC-23 and audit-trail BR-23/AC-23: "Reports" and "Audit log" only for these roles.
      // technician-messages BR-15: "Messages" (after Schedule) only for Owner, Operations Manager, Dispatcher.
      const internal = ['owner', 'operations_manager', 'dispatcher', 'accounting', 'viewer'];
      const visible: Record<string, boolean> = {
        Requests: internal.includes(code),
        Quotes: internal.includes(code),
        Jobs: ['owner', 'operations_manager', 'dispatcher', 'viewer'].includes(code),
        Schedule: ['owner', 'operations_manager', 'dispatcher', 'viewer'].includes(code),
        Messages: MESSAGES_ROLES.includes(code),
        Customers: internal.includes(code),
        Team: ['owner', 'operations_manager', 'dispatcher', 'technician'].includes(code),
        'Products and services': internal.includes(code),
        Invoices: internal.includes(code),
        Reports: ['owner', 'operations_manager', 'dispatcher', 'accounting'].includes(code),
        'Audit log': ['owner', 'operations_manager', 'accounting'].includes(code),
      };
      const operations = MODULE_LABELS.slice(1);
      const modules = [
        ...operations.slice(0, 4),
        'Messages',
        ...operations.slice(4),
        'Reports',
        'Audit log',
      ].filter((label) => visible[label]);
      const expected =
        code === 'technician'
          ? [first, "Today's jobs", ...modules]
          : [first, ...modules, 'Notifications'];
      expect(linkLabels()).toEqual(
        canSeeAdministration ? [...expected, 'Administration'] : expected,
      );
      // BR-17/BR-26: the technician keeps the Coming soon bell link; internal roles get the popover button.
      const bell = host.querySelector('[aria-label="Notifications"]');
      expect(bell?.tagName).toBe(code === 'technician' ? 'A' : 'BUTTON');
      expect(bell?.getAttribute('href')).toBe(
        code === 'technician' ? '/coming-soon/notifications' : null,
      );
      // A heading is shown only when one of its items is.
      const headings = [
        ['Operations', ['Requests', 'Quotes', 'Jobs', 'Schedule', 'Messages']],
        ['People', ['Customers', 'Team']],
        ['Finance', ['Products and services', 'Invoices', 'Reports', 'Audit log']],
      ] as const;
      expect(
        Array.from(host.querySelectorAll('.sidebar__group-label'), (n) => n.textContent),
      ).toEqual(
        headings
          .filter(([, items]) => items.some((label) => visible[label]))
          .map(([heading]) => heading),
      );
      expect(host.querySelector('nav a i')?.classList.contains('pi-th-large')).toBe(true);
      expect(host.querySelectorAll('header')).toHaveLength(1);
      expect(host.querySelectorAll('main')).toHaveLength(1);
      expect(host.querySelector('nav')?.getAttribute('aria-label')).toBe('Main navigation');
      expect(host.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe(first);

      if (visible['Jobs']) {
        await router.navigateByUrl('/jobs/job-1');
        await settle();
        expect(host.querySelectorAll('nav [aria-current="page"]')).toHaveLength(1);
        expect(host.querySelector('nav [aria-current="page"]')?.textContent?.trim()).toBe('Jobs');
        expect(host.querySelector('nav [aria-current="page"]')?.getAttribute('href')).toBe('/jobs');
      }

      if (visible['Products and services']) {
        await router.navigateByUrl('/admin/products-services');
        await settle();
        expect(host.querySelectorAll('nav [aria-current="page"]')).toHaveLength(1);
        expect(host.querySelector('nav [aria-current="page"]')?.textContent?.trim()).toBe(
          'Products and services',
        );
      }

      if (canSeeAdministration) {
        await router.navigateByUrl('/admin/company');
        await settle();
        expect(host.querySelector('nav [aria-current="page"]')?.textContent?.trim()).toBe(
          'Administration',
        );
      }
      expect(host.textContent).not.toContain('Open navigation');
    },
  );

  it('shows the top bar controls, organization menu and Coming soon destinations without requests (FR-04, AC-01, AC-03)', async () => {
    await setup();

    const combobox = host.querySelector('[role="search"] input[role="combobox"]');
    expect(combobox?.getAttribute('placeholder')).toBe('Search requests, customers, work orders…');
    expect(host.querySelector('a[aria-label="Help"]')?.getAttribute('href')).toBe('/help');
    const bell = host.querySelector('button[aria-label="Notifications"]')!;
    expect(bell.getAttribute('aria-haspopup')).toBe('dialog');
    expect(bell.textContent?.trim()).toBe('');

    orgButton()!.click();
    await settle();
    expect(host.querySelectorAll('[role="menuitem"]')).toHaveLength(1);
    expect(menuItem().getAttribute('aria-current')).toBe('true');
    expect(menuItem().textContent).toContain('FieldOps Services');
    menuItem().click();
    await settle();
    expect(host.querySelector('[role="menu"]')).toBeNull();
    expect(router.url).toBe('/dashboard');

    // global-search BR-16: Enter without an active result never opens Coming soon.
    const search = host.querySelector<HTMLInputElement>('#shell-search')!;
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();
    expect(router.url).toBe('/dashboard');
    await expectAxeClean();
  });

  it.each([
    ['full', false],
    ['rail', true],
  ] as const)(
    'starts as %s (rail = %s) and toggles Collapse for the visit only, with names in the rail (FR-01, FR-02, AC-05)',
    async (mode, startsRail) => {
      await setup(SESSION, mode);
      const sidebar = () => host.querySelector('.sidebar')!;
      const collapse = () => host.querySelector<HTMLButtonElement>('.sidebar__collapse')!;
      expect(sidebar().classList.contains('sidebar--rail')).toBe(startsRail);

      collapse().click();
      await settle();
      expect(sidebar().classList.contains('sidebar--rail')).toBe(!startsRail);
      if (!startsRail) {
        expect(host.querySelector('.sidebar__group-label')).toBeNull();
        expect(host.querySelector('nav a')?.getAttribute('aria-label')).toBe('Dashboard');
      }

      collapse().click();
      await settle();
      expect(sidebar().classList.contains('sidebar--rail')).toBe(startsRail);
    },
  );

  it('shows organization, full name, role and initials, and follows session reloads (FR-04, BR-02, AC-08, AC-09)', async () => {
    await setup();

    expect(host.querySelector('.shell__org-name')?.textContent?.trim()).toBe('FieldOps Services');
    expect(host.querySelector('.shell__name')?.textContent?.trim()).toBe('Alex Morgan');
    expect(host.querySelector('.shell__role')?.textContent?.trim()).toBe('Owner');
    expect(host.querySelector('.shell__user-button .avatar__initials')?.textContent?.trim()).toBe(
      'AM',
    );
    expect(
      host.querySelector('.shell__user-button .avatar__initials')?.getAttribute('aria-hidden'),
    ).toBe('true');
    expect(userButton().getAttribute('aria-label')).toBe('Account menu, Alex Morgan, Owner');

    load({
      user: { ...SESSION.user, firstName: 'alex', lastName: '  ' },
      organization: { id: 'o-1', name: 'Renamed Co' },
      role: { code: 'owner', name: 'Owner' },
    });
    await settle();

    expect(host.querySelector('.shell__org-name')?.textContent?.trim()).toBe('Renamed Co');
    expect(host.querySelector('.shell__user-button .avatar__initials')?.textContent?.trim()).toBe(
      'A',
    );
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
    signOutItem().click();
    signOutItem().click();
    await settle();
    expect(signOutItem().querySelector('svg[data-p-icon="spinner"]')).not.toBeNull();

    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    const alert = host.querySelector('p-message');
    expect(alert?.textContent?.trim()).toBe(SIGN_OUT_ERROR_MESSAGE());
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(router.url).toBe('/dashboard');
    expect(sessionService.session()).toEqual(SESSION);
    expect(host.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(userButton());
    await expectAxeClean();
    // Two axe passes over the menu: allow more time than the default under a loaded full run.
  }, 15000);

  it.each(['owner', 'dispatcher', 'technician'])(
    'lists the avatar header, My profile, Account settings, Help & support and Sign out for %s, and the links go to their pages (FR-03, AC-01; help-support-center FR-01, AC-01)',
    async (code) => {
      await setup({ ...SESSION, role: { code, name: 'Role name' } });

      userButton().click();
      await settle();

      const menu = host.querySelector('[aria-label="Account"]')!;
      expect(menu.querySelector('.shell__menu-header app-user-avatar')).not.toBeNull();
      expect(menu.querySelector('.shell__menu-header')?.textContent).toContain('Alex Morgan');
      // BR-14: a known role code shows its translated name, not the server name.
      expect(menu.querySelector('.shell__menu-header')?.textContent).toContain(
        { owner: 'Owner', dispatcher: 'Dispatcher', technician: 'Technician' }[code],
      );
      expect(accountItems().map((item) => item.textContent?.trim())).toEqual([
        'My profile',
        'Account settings',
        'Help & support',
        expect.stringContaining('Language'),
        'Sign out',
      ]);
      expect(menu.querySelector('app-language-menu-item')).not.toBeNull();
      expect(menu.querySelector('[role="separator"]')).not.toBeNull();
      expect(menu.textContent).not.toMatch(/Dark mode/);
      expect(accountItems()[2].getAttribute('href')).toBe('/help');

      // Roving keyboard focus over the items, then Account settings ends on My profile page.
      menuItem().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
      expect(document.activeElement).toBe(signOutItem());
      accountItems()[1].click();
      await settle();
      expect(router.url).toBe('/account/profile');
      expect(host.querySelector('[role="menu"]')).toBeNull();
    },
  );

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

  it.each([
    [0, 'Notifications', null],
    [7, 'Notifications, 7 unread', '7'],
    [120, 'Notifications, 120 unread', '99+'],
  ])(
    'shows the unread badge for %s and marks Notifications current on /notifications (AC-19, AC-21)',
    async (unread, label, badge) => {
      await setup(SESSION, 'full', unread);

      const bell = host.querySelector('button[aria-label^="Notifications"]')!;
      expect(bell.getAttribute('aria-label')).toBe(label);
      expect(bell.getAttribute('aria-expanded')).toBe('false');
      expect(bell.querySelector('.shell__badge')?.textContent?.trim() ?? null).toBe(badge);

      await router.navigateByUrl('/notifications');
      await settle();
      const current = host.querySelector('nav [aria-current="page"]');
      expect(current?.textContent?.trim()).toBe('Notifications');
      expect(current?.getAttribute('href')).toBe('/notifications');
      // Navigation within 30 s reuses the count: no request (afterEach verifies).
    },
  );

  it.each([
    [0, null],
    [120, '99+'],
  ])(
    'shows the Messages badge for %s with a text equivalent and marks it current on conversations (technician-messages AC-15)',
    async (messages, badge) => {
      await setup(SESSION, 'full', 0, messages);

      const link = () => host.querySelector<HTMLAnchorElement>('nav a[href="/messages"]')!;
      expect(link().querySelector('.sidebar__badge')?.textContent?.trim() ?? null).toBe(badge);
      expect(link().textContent).toContain(
        messages === 0 ? 'Messages' : `${messages} unread messages`,
      );
      await router.navigateByUrl('/messages/c-1');
      await settle();
      expect(link().getAttribute('aria-current')).toBe('page');
      // The staff bell keeps counting notifications only.
      expect(
        host.querySelector('button[aria-label^="Notifications"]')?.getAttribute('aria-label'),
      ).toBe('Notifications');
    },
  );

  it.each([
    ['owner', true],
    ['technician', false],
  ])('polls the unread count every 30 s for %s only when internal (AC-20)', async (role, polls) => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      await setup({ ...SESSION, role: { code: role, name: role } });
      await vi.advanceTimersByTimeAsync(30_000);
      if (polls) {
        httpTesting.expectOne({ method: 'GET', url: SUMMARY_URL }).flush({ unread: 2, today: 0 });
        httpTesting
          .expectOne({ method: 'GET', url: MESSAGES_SUMMARY_URL })
          .flush({ unreadMessages: 1, unreadConversations: 1 });
      } else {
        httpTesting.expectNone(SUMMARY_URL);
        httpTesting.expectNone(MESSAGES_SUMMARY_URL);
      }
    } finally {
      vi.useRealTimers();
    }
  });

  it('opens a labelled navigation drawer below md, closes it on selection and returns focus (FR-06, AC-16, AC-17, AC-18)', async () => {
    await setup(SESSION, 'drawer');

    expect(host.querySelector('.sidebar')).toBeNull();
    expect(host.querySelector('app-global-search')).toBeNull();
    expect(host.querySelector('.shell__org-button')).toBeNull();
    expect(host.querySelector('a[aria-label="Help"]')).toBeNull();
    expect(host.querySelector('button[aria-label="Notifications"]')).not.toBeNull();
    const navButton = host.querySelector<HTMLButtonElement>('button[aria-haspopup="dialog"]')!;
    expect(navButton.textContent).toContain('Open navigation');

    navButton.click();
    await settle();

    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(dialog?.getAttribute('aria-labelledby')).toBe('shell-drawer-title');
    expect(dialog?.querySelector('nav')?.getAttribute('aria-label')).toBe('Main navigation');
    expect(dialog?.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Dashboard');
    expect(dialog?.textContent).toContain('Close navigation');
    await expectAxeClean();

    const links = dialog!.querySelectorAll<HTMLAnchorElement>('nav a');
    links[links.length - 1].click();
    await settle();

    expect(router.url).toBe('/admin/company');
    expect((harness.routeDebugElement?.componentInstance as AppShell).drawerOpen()).toBe(false);
  });
});
