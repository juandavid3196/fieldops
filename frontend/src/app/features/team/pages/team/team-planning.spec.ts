import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';
import { BehaviorSubject, map } from 'rxjs';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { AvailabilityDrawer } from '../../components/availability-drawer/availability-drawer';
import { AvailabilityTab } from '../../components/availability-tab/availability-tab';
import { SkillsDrawer } from '../../components/skills-drawer/skills-drawer';
import { SkillsTab } from '../../components/skills-tab/skills-tab';
import {
  AvailabilityPage,
  SkillsMatrixPage,
  TechnicianAvailability,
} from '../../models/team-planning.model';
import { weekRangeLabel, weeklyHours } from '../../utils/team-planning';
import { Team } from './team';

const API = 'http://api.test';
const BRANCH = { id: 'b-1', name: 'Austin Central' };

@Component({ template: '<p>stub</p>' })
class Stub {}

const day = (date: string, state: 'available' | 'time_off' | 'unavailable') => ({
  date,
  state,
  start: state === 'available' ? '08:00' : null,
  end: state === 'available' ? '17:00' : null,
});
const DATES = [
  '2026-10-19',
  '2026-10-20',
  '2026-10-21',
  '2026-10-22',
  '2026-10-23',
  '2026-10-24',
  '2026-10-25',
];
const GRID: AvailabilityPage = {
  weekStart: '2026-10-19',
  items: [
    {
      technicianId: 't-1',
      firstName: 'Manuel',
      lastName: 'Merlano',
      colorHex: null,
      branch: BRANCH,
      timezone: 'America/Chicago',
      usesBranchHours: true,
      days: DATES.map((date, index) => day(date, index < 5 ? 'available' : 'unavailable')),
      weeklyMinutes: 2700,
    },
    {
      technicianId: 't-2',
      firstName: 'Mariana',
      lastName: 'Cardona',
      colorHex: '#0d9488',
      branch: BRANCH,
      timezone: 'America/Chicago',
      usesBranchHours: false,
      days: DATES.map((date, index) => day(date, index === 2 ? 'time_off' : 'available')),
      weeklyMinutes: 2250,
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 10,
};
const MATRIX: SkillsMatrixPage = {
  skills: [
    { id: 's-1', name: 'Plumbing' },
    { id: 's-2', name: 'HVAC' },
  ],
  items: [
    {
      technicianId: 't-1',
      firstName: 'Manuel',
      lastName: 'Merlano',
      colorHex: null,
      branch: BRANCH,
      skillIds: ['s-1'],
      skillCount: 1,
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 10,
};
const OPTIONS = { branches: [BRANCH], skills: [{ id: 's-1', name: 'Plumbing' }] };
const LIST = {
  items: [{ id: 't-1', fullName: 'Manuel Merlano' }],
  totalCount: 1,
  teamMembersCount: 4,
  page: 1,
  pageSize: 10,
};
const availabilityDetail = (
  overrides: Partial<TechnicianAvailability> = {},
): TechnicianAvailability => ({
  technicianId: 't-1',
  firstName: 'Manuel',
  lastName: 'Merlano',
  branch: BRANCH,
  timezone: 'America/Chicago',
  timezoneLabel: '(UTC-05:00) America/Chicago',
  usesBranchHours: true,
  updatedAt: 'v1',
  days: (
    ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const
  ).map((dayOfWeek, index) => ({
    dayOfWeek,
    isAvailable: index < 5,
    start: index < 5 ? '08:00' : null,
    end: index < 5 ? '17:00' : null,
  })),
  timeOff: [],
  ...overrides,
});

describe('Team availability and skills tabs', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<Team>;
  let tab$: BehaviorSubject<string | null>;
  let host: HTMLElement;

  const request = (method: string, path: string, match?: (r: TestRequest['request']) => boolean) =>
    httpTesting.expectOne(
      (r) => r.method === method && r.url === `${API}/${path}` && (match?.(r) ?? true),
    );
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const control = (label: string): HTMLElement | undefined =>
    Array.from(host.querySelectorAll<HTMLElement>('button, a')).find(
      (element) => element.textContent?.trim() === label,
    );
  const byLabel = (label: string): HTMLElement | null =>
    host.querySelector<HTMLElement>(`[aria-label="${label}"]`);

  async function setup(role: string, tab: string | null): Promise<void> {
    tab$ = new BehaviorSubject<string | null>(tab);
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'auth/sign-in', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { data: {} },
            queryParamMap: tab$.pipe(
              map((value) => convertToParamMap(value === null ? {} : { tab: value })),
            ),
          },
        },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    request('GET', 'sessions/current').flush({
      user: {
        id: 'u-1',
        firstName: 'Alex',
        lastName: 'Morgan',
        email: 'a@b.c',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    fixture = TestBed.createComponent(Team);
    host = fixture.nativeElement as HTMLElement;
    await settle();
    request('GET', 'team/options').flush(OPTIONS);
    request('GET', 'team/technicians').flush({ ...LIST, items: [] });
    request('GET', 'team/metrics').flush({
      activeProfiles: 4,
      availableNow: 1,
      onJobs: 1,
      atCapacity: 0,
      unlinkedAccounts: 0,
      capacityAlert: null,
      unlinkedAlert: null,
    });
    request('GET', 'team/skill-coverage').flush([]);
    await settle();
  }

  afterEach(() => httpTesting.verify());

  it('keeps the selected tab in the tab query parameter and leaves the Team page intact (FR-01, AC-01)', async () => {
    await setup('owner', null);
    expect(text()).toContain('Active profiles');
    expect(control('Availability')?.getAttribute('href')).toBe('/team?tab=availability');
    expect(control('Skills')?.getAttribute('href')).toBe('/team?tab=skills');
    expect(host.querySelector('app-team-table, app-availability-tab, app-skills-tab')).toBeNull();

    tab$.next('availability');
    await settle();
    request('GET', 'team/availability', (r) => !r.params.has('weekStart')).flush(GRID);
    await settle();
    expect(host.querySelector('app-availability-tab')).not.toBeNull();
    expect(host.querySelector('[aria-current="page"]')?.textContent).toContain('Availability');
    expect(text()).toContain('Active profiles');
    expect(text()).toContain('Skill coverage');

    tab$.next('skills');
    await settle();
    request('GET', 'team/skills-matrix').flush(MATRIX);
    await settle();
    expect(host.querySelector('app-skills-tab')).not.toBeNull();
    expect(host.querySelector('app-availability-tab')).toBeNull();

    tab$.next(null);
    await settle();
    expect(host.querySelector('app-skills-tab')).toBeNull();
  });

  it('renders the week grid, navigates weeks, filters and shows loading/empty/error states (FR-03, FR-12, AC-23)', async () => {
    await setup('owner', 'availability');
    request('GET', 'team/availability').flush(GRID);
    await settle();
    for (const expected of [
      'Oct 19 – 25, 2026',
      'Mon 19',
      'Sun 25',
      'Manuel Merlano',
      'Austin Central',
      '8:00 AM – 5:00 PM',
      'Time off',
      'Unavailable',
      '45 h',
      '37.5 h',
      'Showing 1 – 2 of 2 technicians',
    ]) {
      expect(text()).toContain(expected);
    }
    expect(host.querySelectorAll('.avail__legend-item')).toHaveLength(3);
    expect(control('Time off')).toBeDefined();
    expect(control('Edit availability')).toBeDefined();

    byLabel('Next week')!.click();
    await settle();
    expect(host.querySelector('.avail__skeletons')).not.toBeNull();
    request('GET', 'team/availability', (r) => r.params.get('weekStart') === '2026-10-26').flush({
      ...GRID,
      weekStart: '2026-10-26',
      items: [],
      totalCount: 0,
    });
    await settle();
    expect(text()).toContain('Oct 26 – Nov 1, 2026');
    expect(text()).toContain('No active technicians yet.');

    control('This week')!.click();
    request('GET', 'team/availability', (r) => !r.params.has('weekStart')).flush(GRID);
    await settle();

    const tab = fixture.debugElement.query(By.directive(AvailabilityTab))
      .componentInstance as AvailabilityTab;
    tab.onSearchText('ana');
    await new Promise((resolve) => setTimeout(resolve, 350));
    request('GET', 'team/availability', (r) => r.params.get('search') === 'ana').flush({
      ...GRID,
      items: [],
      totalCount: 0,
    });
    tab.onAvailability('time_off');
    request(
      'GET',
      'team/availability',
      (r) => r.params.get('availability') === 'time_off' && r.params.get('page') === '1',
    ).flush({ ...GRID, items: [], totalCount: 0 });
    await settle();
    expect(text()).toContain('No technicians match these filters.');
    control('Clear filters')!.click();
    request(
      'GET',
      'team/availability',
      (r) => r.params.get('availability') === 'all' && !r.params.has('search'),
    ).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load availability. Try again.");
    control('Retry')!.click();
    request('GET', 'team/availability').flush(GRID);
    await settle();
    expect(weekRangeLabel('2026-12-28')).toBe('Dec 28, 2026 – Jan 3, 2027');
    expect(weeklyHours(2250)).toBe('37.5 h');
  });

  it('renders the skills matrix with accessible cells, sorting, empty catalog and role-based actions (FR-08, AC-15, AC-19, AC-20)', async () => {
    await setup('owner', 'skills');
    request('GET', 'team/skills-matrix').flush(MATRIX);
    await settle();
    expect(
      Array.from(host.querySelectorAll('thead th')).map((th) => th.textContent?.trim()),
    ).toEqual(['Technician', 'Home branch', 'Plumbing', 'HVAC', 'Skills', 'Actions']);
    expect(byLabel('Plumbing: assigned')).not.toBeNull();
    expect(byLabel('HVAC: not assigned')).not.toBeNull();
    expect(host.querySelector('th.matrix__sticky')).not.toBeNull();
    expect(text()).toContain('Skills help Dispatch match technicians to the right jobs.');
    expect(text()).toContain('Showing 1 – 1 of 1 technicians');
    expect(control('Edit skills')).toBeDefined();
    expect(byLabel('Actions for Manuel Merlano')).not.toBeNull();

    control('Technician')!.click();
    request('GET', 'team/skills-matrix', (r) => r.params.get('sort') === 'desc').flush({
      ...MATRIX,
      skills: [],
    });
    await settle();
    expect(text()).toContain('No skills defined yet.');
    expect(control('Skill catalog')).toBeDefined();

    TestBed.resetTestingModule();
    await setup('dispatcher', 'skills');
    request('GET', 'team/skills-matrix').flush(MATRIX);
    await settle();
    expect(text()).not.toContain('Actions');
    for (const label of ['Skill catalog', 'Edit skills']) {
      expect(control(label)).toBeUndefined();
    }
    expect(host.querySelector('app-skills-drawer')).toBeNull();
    expect(fixture.debugElement.query(By.directive(SkillsTab))).not.toBeNull();

    TestBed.resetTestingModule();
    await setup('dispatcher', 'availability');
    request('GET', 'team/availability').flush(GRID);
    await settle();
    expect(control('Time off')).toBeUndefined();
    expect(control('Edit availability')).toBeUndefined();
    expect(host.querySelector('app-availability-drawer')).toBeNull();
  });
});

describe('Availability and skills drawers', () => {
  let httpTesting: HttpTestingController;
  let confirmations: Confirmation[];
  let toasts: ReturnType<typeof vi.spyOn>;

  function configure(): void {
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
    confirmations = [];
    vi.spyOn(TestBed.inject(ConfirmationService), 'confirm').mockImplementation((confirmation) => {
      confirmations.push(confirmation);
      return undefined as never;
    });
    toasts = vi.spyOn(TestBed.inject(MessageService), 'add');
  }
  const flushPickers = (): void =>
    httpTesting
      .match((r) => r.url === `${API}/team/technicians` && r.method === 'GET')
      .forEach((r) => r.flush(LIST));
  const get = (path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}/${path}`);

  afterEach(() => httpTesting.verify());

  it('edits availability with dirty/discard, single submission, stale Reload and Use branch hours (FR-05, FR-06, AC-06, AC-10, AC-23)', async () => {
    configure();
    const fixture = TestBed.createComponent(AvailabilityDrawer);
    const drawer = fixture.componentInstance;
    const closed = vi.fn();
    const saved = vi.fn();
    drawer.closed.subscribe(closed);
    drawer.saved.subscribe(saved);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    flushPickers();

    drawer.onPick('t-1');
    get('team/technicians/t-1/availability').flush(
      availabilityDetail({
        days: availabilityDetail().days.map((d) =>
          d.dayOfWeek === 'monday' ? { ...d, start: '08:15' } : d,
        ),
      }),
    );
    expect(drawer.scheduleLabel()).toBe('Using Austin Central hours');
    expect(drawer.canSave()).toBe(false);
    expect(drawer.rows()[0].startChoices.some((choice) => choice.value === '08:15')).toBe(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Time zone: (UTC-05:00) America/Chicago');

    drawer.patch('tuesday', { isAvailable: false });
    expect(drawer.canSave()).toBe(true);
    drawer.patch('wednesday', { start: '18:00' });
    expect(drawer.valid()).toBe(false);
    expect(drawer.rows()[2].error.end).toBe('End time must be after start time.');
    drawer.patch('wednesday', { start: '09:00' });

    // Switching technician while dirty asks first and keeps the current one on reject.
    drawer.onPick('t-9');
    expect(confirmations.at(-1)?.key).toBe('discard-changes');
    confirmations.at(-1)!.reject!();
    expect(drawer.selection()).toBe('t-1');

    drawer.submit();
    drawer.submit();
    const put = httpTesting.expectOne((r) => r.method === 'PUT');
    expect(put.request.body.updatedAt).toBe('v1');
    expect(put.request.body.days[0]).toEqual({
      dayOfWeek: 'monday',
      isAvailable: true,
      start: '08:15',
      end: '17:00',
    });
    expect(put.request.body.days[1]).toEqual({
      dayOfWeek: 'tuesday',
      isAvailable: false,
      start: null,
      end: null,
    });
    put.flush({ code: 'stale' }, { status: 409, statusText: 'Conflict' });
    expect(drawer.stale()).toBe(true);
    expect(drawer.dirty()).toBe(true);
    expect(closed).not.toHaveBeenCalled();

    drawer.load();
    get('team/technicians/t-1/availability').flush(
      availabilityDetail({ usesBranchHours: false, updatedAt: 'v2' }),
    );
    expect(drawer.stale()).toBe(false);
    expect(drawer.dirty()).toBe(false);

    drawer.patch('friday', { isAvailable: false });
    drawer.requestClose();
    expect(confirmations.at(-1)?.key).toBe('discard-changes');
    expect(closed).not.toHaveBeenCalled();
    confirmations.at(-1)!.accept!();
    expect(closed).toHaveBeenCalledTimes(1);

    drawer.useBranchHours();
    expect(confirmations.at(-1)?.header).toBe('Use Austin Central hours?');
    expect(confirmations.at(-1)?.message).toContain("Manuel Merlano's custom schedule");
    confirmations.at(-1)!.accept!();
    const post = httpTesting.expectOne(
      (r) => r.method === 'POST' && r.url.endsWith('/availability/use-branch-hours'),
    );
    expect(post.request.body).toEqual({ updatedAt: 'v2' });
    post.flush(availabilityDetail({ updatedAt: 'v3' }));
    expect(toasts).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'Branch hours applied.' }),
    );
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('edits skills with debounced search, selection count, single save and stale Reload (FR-09, AC-17, AC-23)', async () => {
    configure();
    const fixture = TestBed.createComponent(SkillsDrawer);
    const drawer = fixture.componentInstance;
    const saved = vi.fn();
    drawer.saved.subscribe(saved);
    const check = (id: string, checked: boolean): void =>
      drawer.toggle(id, { target: { checked } } as unknown as Event);
    fixture.componentRef.setInput('initialTechnicianId', 't-2');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    flushPickers();
    get('team/skills').flush([
      { id: 's-2', name: 'HVAC', isActive: true },
      { id: 's-1', name: 'Plumbing', isActive: true },
      { id: 's-3', name: 'Archived', isActive: false },
    ]);
    get('team/technicians/t-2/skill-assignments').flush({
      technicianId: 't-2',
      firstName: 'Mariana',
      lastName: 'Cardona',
      updatedAt: 'k1',
      skillIds: ['s-1'],
    });
    expect(drawer.visibleSkills().map((skill) => skill.name)).toEqual(['HVAC', 'Plumbing']);
    expect(drawer.canSave()).toBe(false);

    drawer.onSearch('hv');
    expect(drawer.visibleSkills()).toHaveLength(2);
    await new Promise((resolve) => setTimeout(resolve, 350));
    expect(drawer.visibleSkills().map((skill) => skill.name)).toEqual(['HVAC']);

    check('s-2', true);
    check('s-1', false);
    check('s-1', true);
    expect(drawer.selectedCount()).toBe(2);
    expect(drawer.canSave()).toBe(true);
    drawer.submit();
    drawer.submit();
    const put = httpTesting.expectOne((r) => r.method === 'PUT');
    expect(put.request.body).toEqual({ updatedAt: 'k1', skillIds: ['s-1', 's-2'] });
    put.flush({ code: 'stale' }, { status: 409, statusText: 'Conflict' });
    expect(drawer.stale()).toBe(true);
    expect(saved).not.toHaveBeenCalled();

    drawer.load();
    get('team/technicians/t-2/skill-assignments').flush({
      technicianId: 't-2',
      firstName: 'Mariana',
      lastName: 'Cardona',
      updatedAt: 'k2',
      skillIds: ['s-1'],
    });
    check('s-2', true);
    drawer.submit();
    httpTesting
      .expectOne((r) => r.method === 'PUT')
      .flush({
        technicianId: 't-2',
        firstName: 'Mariana',
        lastName: 'Cardona',
        updatedAt: 'k3',
        skillIds: ['s-1', 's-2'],
      });
    expect(toasts).toHaveBeenCalledWith(expect.objectContaining({ summary: 'Skills saved.' }));
    expect(saved).toHaveBeenCalledTimes(1);
  });
});
