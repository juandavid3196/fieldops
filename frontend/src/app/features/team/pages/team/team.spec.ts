import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { TechnicianFormDrawer } from '../../components/technician-form-drawer/technician-form-drawer';
import { validateTechnicianField } from '../../components/technician-form-drawer/technician-form.validators';
import { TechnicianProfile } from '../../components/technician-profile/technician-profile';
import {
  TeamMetrics,
  TechnicianDetail,
  TechnicianListResponse,
  TechnicianRow,
} from '../../models/team.model';
import { groupAvailability } from '../../utils/team-format';
import { Team } from './team';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

const ANA: TechnicianRow = {
  id: 't-1',
  fullName: 'Ana López',
  branchName: 'Austin Central',
  skills: ['Plumbing', 'HVAC'],
  isLinked: true,
  profileStatus: 'active',
  todayStatus: 'on_job',
  jobs: 6,
  workloadPercent: 110,
  workloadState: 'percent',
  atCapacity: true,
  nextAvailable: { kind: 'now' },
  timezone: 'America/Chicago',
};
const ETHAN: TechnicianRow = {
  ...ANA,
  id: 't-2',
  fullName: 'Ethan Wright',
  skills: [],
  isLinked: false,
  todayStatus: 'available',
  jobs: 1,
  workloadPercent: 20,
  atCapacity: false,
};
const OLD: TechnicianRow = {
  ...ETHAN,
  id: 't-3',
  fullName: 'Old Profile',
  profileStatus: 'inactive',
  todayStatus: 'inactive',
};
const METRICS: TeamMetrics = {
  activeProfiles: 18,
  availableNow: 4,
  onJobs: 11,
  atCapacity: 2,
  unlinkedAccounts: 1,
  capacityAlert: {
    technicianId: 't-1',
    name: 'Ana López',
    workloadPercent: 110,
    noAvailability: false,
    moreCount: 1,
  },
  unlinkedAlert: { technicianId: 't-2', name: 'Ethan Wright', moreCount: 2 },
};
const OPTIONS = {
  branches: [
    { id: 'b-1', name: 'Austin Central' },
    { id: 'b-2', name: 'Round Rock' },
  ],
  skills: [{ id: 's-1', name: 'Plumbing' }],
};
const COVERAGE = [
  { skillId: 's-1', name: 'Plumbing', technicianCount: 7, health: 'healthy' },
  { skillId: 's-2', name: 'Landscaping', technicianCount: 3, health: 'low' },
];
const listBody = (
  items: readonly TechnicianRow[],
  total = items.length,
): TechnicianListResponse => ({
  items,
  totalCount: total,
  teamMembersCount: 18,
  page: 1,
  pageSize: 10,
});
const windows = [{ start: '08:00:00', end: '17:00:00' }];
const DETAIL: TechnicianDetail = {
  id: 't-1',
  firstName: 'Ana',
  lastName: 'López',
  email: 'ana@example.com',
  phone: null,
  employeeCode: 'TECH-014',
  notes: 'Prefers mornings',
  branch: { id: 'b-1', name: 'Austin Central' },
  profileStatus: 'active',
  account: { email: 'ana@fieldops.com', roleName: 'Technician' },
  skills: [
    { name: 'Plumbing', proficiency: 4, isPrimary: true },
    { name: 'HVAC', proficiency: null, isPrimary: false },
  ],
  availability: [1, 2, 3, 4, 5].map((dayOfWeek) => ({ dayOfWeek, windows })),
  today: {
    todayStatus: 'on_job',
    currentJob: { label: 'WO-1051 – Water heater issue' },
    jobs: 4,
    workloadPercent: 80,
    workloadState: 'percent',
    nextAvailable: { kind: 'now' },
  },
  timezone: 'America/Chicago',
};

describe('Team page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<Team>;
  let page: Team;
  let host: HTMLElement;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLElement | undefined =>
    Array.from(host.querySelectorAll<HTMLElement>('button, a')).find(
      (element) => element.textContent?.trim() === label,
    );

  async function setup(
    roleCode: string,
    init: (rows?: readonly TechnicianRow[]) => void = () => undefined,
  ) {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'auth/sign-in', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    call('GET', 'sessions/current').flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: roleCode },
    });
    fixture = TestBed.createComponent(Team);
    page = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    init();
    await settle();
  }

  const flushManager = (rows: readonly TechnicianRow[] = [ANA, ETHAN, OLD]): void => {
    call('GET', 'team/options').flush(OPTIONS);
    call('GET', 'team/technicians').flush(listBody(rows, 18));
    call('GET', 'team/metrics').flush(METRICS);
    call('GET', 'team/skill-coverage').flush(COVERAGE);
  };

  afterEach(() => httpTesting.verify());

  it.each(['accounting', 'viewer', 'unknown_role'])(
    'shows the forbidden state with no data requests for %s (AC-20)',
    async (role) => {
      await setup(role);
      expect(text()).toContain("You don't have access to Team.");
      expect(host.querySelector('app-team-table')).toBeNull();
      expect(button('Add technician profile')).toBeUndefined();
    },
  );

  it('gives mutators the full page and Dispatcher a read-only one (FR-01, AC-01, AC-21, AC-24)', async () => {
    await setup('owner', () => flushManager());
    expect(text()).toContain('Sign-in access and permissions are managed in Settings.');
    for (const label of [
      'Active profiles',
      'Available now',
      'On jobs',
      'At capacity',
      'Unlinked accounts',
    ]) {
      expect(text()).toContain(label);
    }
    expect(text()).toContain('Team members (18)');
    expect(text()).toContain('Showing 1 – 10 of 18 technicians');
    expect(text()).toContain('Plumbing, HVAC');
    expect(text()).toContain('110%');
    expect(text()).toContain('Not linked');
    expect(button('Add technician profile')).toBeDefined();
    expect(page.buildMenu(ANA).map((item) => item.label)).toEqual([
      'View',
      'Edit',
      'Unlink account',
      'Deactivate',
    ]);
    expect(page.buildMenu(ETHAN).map((item) => item.label)).toContain('Link account');
    expect(page.buildMenu(OLD).map((item) => item.label)).toContain('Activate');
    expect(button('Manage skills')?.getAttribute('href')).toBe('/coming-soon/team-skills');
    expect(button('Availability')?.getAttribute('href')).toBe('/coming-soon/team-availability');
    expect(button('Skills')?.getAttribute('href')).toBe('/coming-soon/team-skills');

    // Dispatcher: same data, no mutation affordances.
    TestBed.resetTestingModule();
    await setup('dispatcher', () => flushManager([ANA, ETHAN]));
    expect(button('Add technician profile')).toBeUndefined();
    expect(button('Link account')).toBeUndefined();
    expect(button('Manage skills')).toBeDefined();
    expect(page.buildMenu(ANA).map((item) => item.label)).toEqual(['View']);
    expect(host.querySelector('app-link-account-dialog')).toBeNull();
    expect(host.querySelector('app-technician-form-drawer')).toBeNull();
  });

  it.each([
    ['linked profile', 200],
    ['unlinked profile', 404],
  ])(
    'renders the Technician own profile as a read-only page: %s (AC-19)',
    async (_name, status) => {
      await setup('technician', () => {
        const request = call('GET', 'team/me');
        if (status === 200) {
          request.flush({ ...DETAIL, notes: undefined });
        } else {
          request.flush({}, { status: 404, statusText: 'Not Found' });
        }
      });
      if (status === 200) {
        expect(text()).toContain('Ana López');
        expect(text()).toContain('Plumbing');
        expect(button('View schedule')?.getAttribute('href')).toBe('/schedule');
        for (const hidden of [
          'Edit skills',
          'Edit availability',
          'Unlink account',
          'View access in Settings',
        ]) {
          expect(button(hidden)).toBeUndefined();
        }
        expect(text()).not.toContain('Prefers mornings');
      } else {
        expect(text()).toContain("Your team profile isn't linked yet. Contact your manager.");
      }
      expect(host.querySelector('app-team-table')).toBeNull();
      expect(button('Add technician profile')).toBeUndefined();
    },
  );

  it('renders the BR-13 detail sections for linked and unlinked profiles (AC-16, AC-24)', async () => {
    await setup('owner', () => flushManager());
    const profile = TestBed.createComponent(TechnicianProfile);
    const root = profile.nativeElement as HTMLElement;
    const flat = () =>
      root.innerHTML
        .replace(/<[^>]+>/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
    profile.componentRef.setInput('detail', {
      ...DETAIL,
      availability: [
        ...DETAIL.availability,
        { dayOfWeek: 6, windows: [] },
        { dayOfWeek: 0, windows: [] },
      ],
    });
    profile.componentRef.setInput('canMutate', true);
    profile.componentRef.setInput('canViewSettings', true);
    profile.detectChanges();

    for (const expected of [
      'Email ana@example.com',
      'Mobile phone —',
      'Profile ID TECH-014',
      'Home branch Austin Central',
      'Linked to user account (read-only)',
      'User email ana@fieldops.com',
      'Account permissions are managed in Settings. Linking a user does not change their permissions.',
      'Plumbing Advanced Primary',
      'Monday – Friday 8:00 AM – 5:00 PM',
      'Weekends Off',
      'Current job WO-1051 – Water heater issue',
      'Workload 80%',
    ]) {
      expect(flat()).toContain(expected);
    }
    const labels = Array.from(root.querySelectorAll<HTMLElement>('button, a')).map((e) =>
      e.textContent?.trim(),
    );
    expect(labels).toEqual(
      expect.arrayContaining([
        'View access in Settings',
        'Unlink account',
        'Edit skills',
        'Edit availability',
      ]),
    );

    profile.componentRef.setInput('detail', {
      ...DETAIL,
      account: null,
      skills: [],
      availability: [],
      email: null,
    });
    profile.componentRef.setInput('canViewSettings', false);
    profile.detectChanges();
    expect(flat()).toContain('No linked user account');
    expect(flat()).toContain('No skills yet');
    expect(flat()).toContain('No availability set');
    expect(flat()).toContain('Email —');
    expect(flat()).not.toContain('View access in Settings');
    expect(flat()).toContain('Link account');

    expect(
      groupAvailability([
        { dayOfWeek: 1, windows },
        { dayOfWeek: 2, windows },
        { dayOfWeek: 3, windows: [{ start: '09:00', end: '13:00' }] },
        { dayOfWeek: 0, windows: [{ start: '10:00', end: '14:00' }] },
      ]).map((line) => `${line.label} ${line.value}`),
    ).toEqual([
      'Monday – Tuesday 8:00 AM – 5:00 PM',
      'Wednesday 9:00 AM – 1:00 PM',
      'Thursday Off',
      'Friday Off',
      'Saturday Off',
      'Sunday 10:00 AM – 2:00 PM',
    ]);
  });

  it('shows capacity and unlinked alerts, the period wording, coverage and states (AC-17, AC-18, AC-22)', async () => {
    await setup('owner', () => flushManager());
    expect(text()).toContain('Ana López is at 110% capacity today. +1 more');
    expect(text()).toContain('Ethan Wright has no linked user account. +2 more');
    expect(text()).toContain('Healthy');
    expect(text()).toContain('Low coverage');
    expect(host.querySelector('.team__dot')).not.toBeNull();

    button('Link account')!.click();
    await settle();
    expect(page.linkOpen()).toBe(true);
    expect(page.linkTarget()).toEqual({ id: 't-2', name: 'Ethan Wright' });
    call('GET', 'team/linkable-accounts').flush([]);
    page.linkOpen.set(false);

    button('This week')!.click();
    call('GET', 'team/metrics').flush({
      ...METRICS,
      capacityAlert: {
        ...METRICS.capacityAlert!,
        workloadPercent: null,
        noAvailability: true,
        moreCount: 0,
      },
      unlinkedAlert: null,
    });
    const weekList = call('GET', 'team/technicians');
    expect(weekList.request.params.get('period')).toBe('week');
    expect(weekList.request.params.get('page')).toBe('1');
    weekList.flush(listBody([ANA], 1));
    await settle();
    expect(text()).toContain('Ana López has no availability this week.');
    expect(text()).not.toContain('has no linked user account');
    expect(text()).toContain('Jobs this week');

    // Filtered empty, then per-section errors with Retry.
    page.onStatus('suspended');
    call('GET', 'team/technicians').flush(listBody([], 0));
    await settle();
    expect(text()).toContain('No technicians match your filters.');
    button('Clear filters')!.click();
    call('GET', 'team/technicians').flush(listBody([], 0));
    await settle();
    expect(text()).toContain('No technician profiles yet.');
    expect(button('Add technician profile')).toBeDefined();

    page.loadList();
    call('GET', 'team/technicians').flush({}, { status: 500, statusText: 'Server Error' });
    page.loadCoverage();
    call('GET', 'team/skill-coverage').flush([]);
    await settle();
    expect(text()).toContain("We couldn't load this section.");
    expect(text()).toContain('No skills defined yet.');
    button('Retry')!.click();
    call('GET', 'team/technicians').flush(listBody([ANA], 1));
  });

  it('confirms status changes, surfaces the 409 count and unlinks (AC-12 UI, FR-05)', async () => {
    await setup('owner', () => flushManager());
    const confirmations: Confirmation[] = [];
    vi.spyOn(fixture.debugElement.injector.get(ConfirmationService), 'confirm').mockImplementation(
      (confirmation) => {
        confirmations.push(confirmation);
        return undefined as never;
      },
    );
    const toasts = vi.spyOn(fixture.debugElement.injector.get(MessageService), 'add');

    page.buildMenu(ANA).find((item) => item.label === 'Deactivate')!.command!({});
    expect(confirmations.at(-1)?.header).toBe('Deactivate Ana López?');
    confirmations.at(-1)!.accept!();
    call('POST', 'team/technicians/t-1/deactivate').flush(
      { upcomingVisitCount: 2 },
      { status: 409, statusText: 'Conflict' },
    );
    expect(toasts).toHaveBeenCalledWith(
      expect.objectContaining({
        severity: 'error',
        summary: "Reassign this technician's upcoming visits before deactivating the profile.",
        detail: '2 upcoming visits are assigned.',
      }),
    );
    flushRefresh();

    page.buildMenu(OLD).find((item) => item.label === 'Activate')!.command!({});
    expect(confirmations.at(-1)?.header).toBe('Activate Old Profile?');
    confirmations.at(-1)!.accept!();
    call('POST', 'team/technicians/t-3/activate').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    expect(toasts).toHaveBeenCalledWith(expect.objectContaining({ summary: 'Profile activated.' }));
    flushRefresh();

    page.buildMenu(ANA).find((item) => item.label === 'Unlink account')!.command!({});
    expect(confirmations.at(-1)?.header).toBe("Unlink Ana López's user account?");
    confirmations.at(-1)!.accept!();
    call('DELETE', 'team/technicians/t-1/account-link').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    expect(toasts).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'User account unlinked.' }),
    );
    flushRefresh();
  });

  function flushRefresh(): void {
    call('GET', 'team/technicians').flush(listBody([ANA, ETHAN, OLD], 18));
    call('GET', 'team/metrics').flush(METRICS);
    call('GET', 'team/skill-coverage').flush(COVERAGE);
  }
});

describe('Technician form drawer', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<TechnicianFormDrawer>;
  let drawer: TechnicianFormDrawer;

  async function setup(mode: 'create' | 'edit' = 'create') {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        MessageService,
        ConfirmationService,
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TechnicianFormDrawer);
    drawer = fixture.componentInstance;
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('branches', OPTIONS.branches);
    fixture.componentRef.setInput('technicianId', mode === 'edit' ? 't-1' : null);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => httpTesting?.verify());

  it.each([
    ['firstName', '   ', 'Enter a first name.'],
    ['lastName', 'x'.repeat(101), 'Use 100 characters or fewer.'],
    ['email', 'not-an-email', 'Enter a valid email address.'],
    ['phone', '12-34', 'Enter a valid phone number.'],
    ['phone', '12345678901234567', 'Enter a valid phone number.'],
    ['phone', '(512) 555-0198', null],
    ['employeeCode', 'bad code', 'Use letters, numbers, hyphens or underscores.'],
    ['notes', 'n'.repeat(2001), 'Use 2000 characters or fewer.'],
    ['branchId', 'b-9', 'Choose a branch you have access to.'],
  ] as const)('validates %s (BR-15 field table)', (field, input, message) => {
    const value = {
      firstName: 'Ana',
      lastName: 'López',
      email: '',
      phone: '',
      employeeCode: '',
      branchId: 'b-1',
      notes: '',
      [field]: input,
    };
    expect(validateTechnicianField(field, value, ['b-1', 'b-2'])).toBe(message);
  });

  it('submits once, maps server errors, guards dirty close and creates (AC-09, AC-10, AC-23)', async () => {
    await setup();
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const toasts = vi.spyOn(TestBed.inject(MessageService), 'add');
    const closed = vi.fn();
    const saved = vi.fn();
    drawer.closed.subscribe(closed);
    drawer.saved.subscribe(saved);

    // Pristine close needs no confirmation; a dirty one uses the shared discard dialog.
    drawer.requestClose();
    expect(closed).toHaveBeenCalledTimes(1);
    drawer.patch({ firstName: 'Ana' }, 'firstName');
    drawer.requestClose();
    expect(confirm.mock.calls.at(-1)![0].header).toBe('Discard unsaved changes?');
    expect(closed).toHaveBeenCalledTimes(1);

    // Required fields block submission.
    drawer.submit();
    expect(drawer.error('lastName')).toBe('Enter a last name.');
    expect(drawer.error('branchId')).toBe('Choose a branch you have access to.');

    drawer.patch(
      { lastName: 'López', branchId: 'b-1', employeeCode: 'TECH-1', email: ' A@x.co ' },
      'lastName',
    );
    drawer.submit();
    drawer.submit();
    expect(drawer.submitting()).toBe(true);
    const request = httpTesting.expectOne(`${API}/team/technicians`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      firstName: 'Ana',
      lastName: 'López',
      email: 'A@x.co',
      phone: null,
      employeeCode: 'TECH-1',
      branchId: 'b-1',
      notes: null,
    });
    request.flush(
      { errors: { employeeCode: ['This profile ID is already in use.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(drawer.submitting()).toBe(false);
    expect(drawer.error('employeeCode')).toBe('This profile ID is already in use.');

    drawer.submit();
    httpTesting
      .expectOne(`${API}/team/technicians`)
      .flush({}, { status: 500, statusText: 'Server Error' });
    expect(toasts).toHaveBeenCalledWith(
      expect.objectContaining({
        severity: 'error',
        summary: expect.stringContaining("couldn't save"),
      }),
    );
    expect(drawer.form().firstName).toBe('Ana');

    drawer.patch({ employeeCode: 'TECH-2' }, 'employeeCode');
    drawer.submit();
    httpTesting
      .expectOne(`${API}/team/technicians`)
      .flush({ id: 't-9' }, { status: 201, statusText: 'Created' });
    expect(toasts).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'Technician profile created.' }),
    );
    expect(saved).toHaveBeenCalledWith('t-9');
  });
});
