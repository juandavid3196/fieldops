import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import {
  exceptionConflictMessage,
  validateException,
} from '../../components/exception-dialog/exception-dialog';
import { DayForm, SkillRow, SkillsAvailability } from '../../models/skills-availability.model';
import {
  addSkill,
  dropInactive,
  breakOptions,
  copyMondayToWeekdays,
  dayHours,
  formatHours,
  pickerSkills,
  removeSkill,
  setPrimary,
  sortSkills,
  splitWeeklyErrors,
  toDayForms,
  toWeeklyRequest,
  utilizationView,
  weeklyTotalHours,
  withWindow,
} from '../../utils/skills-availability';
import { SkillsAvailabilityPage } from './skills-availability';

const API = 'http://api.test';
const PAGE = 'team/technicians/t-1/skills-availability';

const day = (dayOfWeek: number, capacityPercent = 100) => ({
  dayOfWeek,
  start: '08:00',
  end: '17:00',
  breakStart: '12:00',
  breakEnd: '13:00',
  capacityPercent,
});

const DATA: SkillsAvailability = {
  technician: {
    id: 't-1',
    firstName: 'Carlos',
    lastName: 'Rivera',
    profileStatus: 'active',
    branch: { id: 'b-1', name: 'Austin Central' },
  },
  version: 'v1',
  timezone: 'America/Chicago',
  timezoneLabel: '(UTC-05:00) Central Time',
  weeklyAvailability: [day(1), day(2), day(3, 50), day(4)],
  skills: [
    { skillId: 's-2', name: 'HVAC', proficiency: 3, isPrimary: false },
    { skillId: 's-1', name: 'Plumbing', proficiency: 4, isPrimary: true },
  ],
  exceptions: [
    {
      id: 'e-1',
      date: '2099-09-24',
      kind: 'partial',
      start: '13:00',
      end: '15:00',
      reason: 'Personal appointment',
      status: 'active',
      version: 'ev1',
    },
  ],
  upcoming: [
    { date: '2099-09-20', label: 'today', state: 'available_now', windows: [], limited: false },
    {
      date: '2099-09-21',
      label: 'tomorrow',
      state: 'windows',
      windows: [{ start: '08:00', end: '17:00' }],
      limited: false,
    },
    { date: '2099-10-05', label: 'date', state: 'unavailable', windows: [], limited: false },
  ],
  capacity: {
    availableMinutes: 2400,
    scheduledMinutes: 2040,
    remainingMinutes: 360,
    utilizationPercent: 85,
  },
  today: { jobs: 4, bookedPercent: 80 },
};
const EMPTY: SkillsAvailability = {
  ...DATA,
  weeklyAvailability: [],
  skills: [],
  exceptions: [],
  upcoming: [],
  capacity: {
    availableMinutes: 0,
    scheduledMinutes: 0,
    remainingMinutes: 0,
    utilizationPercent: null,
  },
  today: { jobs: 0, bookedPercent: null },
};
const CATALOG = [
  { id: 's-1', name: 'Plumbing', isActive: true },
  { id: 's-3', name: 'Electrical', isActive: true },
  { id: 's-4', name: 'Legacy', isActive: false },
];

const api = (error: Partial<ApiError>): ApiError => ({
  kind: 'unknown',
  status: 0,
  message: '',
  fieldErrors: {},
  ...error,
});

describe('Skills & availability: pure rules', () => {
  it('derives capacity, copy and reset per BR-05 to BR-08', () => {
    const days = toDayForms(DATA.weeklyAvailability);
    const byDay = (value: readonly DayForm[], dow: number): DayForm =>
      value.find((item) => item.dayOfWeek === dow)!;

    // Monday-first, Off days contribute nothing, stored capacity weights hours.
    expect(days.map((item) => item.dayOfWeek)).toEqual([1, 2, 3, 4, 5, 6, 0]);
    expect(formatHours(dayHours(byDay(days, 1)))).toBe('8h');
    expect(formatHours(dayHours(byDay(days, 3)))).toBe('4h');
    expect(formatHours(dayHours(byDay(days, 5)))).toBe('0h');
    expect(weeklyTotalHours(days)).toBe(28);

    // A shorter window that no longer fits the break clears it; 7.5h shows one decimal.
    const shifted = withWindow(byDay(days, 1), { end: '12:30' });
    expect(shifted.breakStart).toBeNull();
    expect(
      formatHours(dayHours({ ...shifted, start: '09:00', end: '16:30', capacityPercent: 100 })),
    ).toBe('7.5h');
    expect(
      breakOptions(shifted).every((option) => option.value === '' || option.value < '12:30'),
    ).toBe(true);

    // Copy Monday: toggle, window, break; capacity untouched; Friday (Off) newly enabled uses 100.
    const edited = days.map((item) =>
      item.dayOfWeek === 1 ? { ...item, start: '09:00', breakStart: null, breakEnd: null } : item,
    );
    const copied = copyMondayToWeekdays(edited);
    expect(byDay(copied, 3)).toMatchObject({
      on: true,
      start: '09:00',
      breakStart: null,
      capacityPercent: 50,
    });
    expect(byDay(copied, 5)).toMatchObject({ on: true, start: '09:00', capacityPercent: 100 });
    expect(weeklyTotalHours(copied)).toBe(8 * 3 + 4 + 8 + 0);
    expect(toWeeklyRequest(copied).map((item) => item.dayOfWeek)).toEqual([1, 2, 3, 4, 5]);

    // Reset restores the saved table.
    expect(toWeeklyRequest(toDayForms(DATA.weeklyAvailability))).toEqual(toWeeklyRequest(days));
    expect(utilizationView(null).text).toBe('—');
    expect(utilizationView(120)).toMatchObject({ fill: 100, over: true });
    expect(splitWeeklyErrors({ 'weeklyAvailability[2].end': ['x'], skills: ['y'] })).toEqual({
      days: { 2: { end: 'x' } },
      rest: { skills: 'y' },
    });
  });

  it('keeps exactly one primary skill and filters the picker per BR-09/BR-10', () => {
    const picker = pickerSkills(CATALOG, [{ skillId: 's-1' } as SkillRow]);
    expect(picker.map((skill) => skill.id)).toEqual(['s-3']);

    let rows: SkillRow[] = [];
    rows = addSkill(rows, { id: 's-9', name: 'Zinc' });
    rows = addSkill(rows, { id: 's-3', name: 'Electrical' });
    rows = addSkill(rows, { id: 's-5', name: 'Alarms' });
    expect(rows.map((row) => [row.name, row.proficiency, row.isPrimary])).toEqual([
      ['Zinc', 3, true],
      ['Electrical', 3, false],
      ['Alarms', 3, false],
    ]);
    expect(sortSkills(setPrimary(rows, 's-3')).map((row) => row.name)).toEqual([
      'Electrical',
      'Alarms',
      'Zinc',
    ]);
    // Removing the primary promotes the remaining skill first by name.
    const promoted = removeSkill(rows, 's-9');
    expect(promoted.filter((row) => row.isPrimary).map((row) => row.name)).toEqual(['Alarms']);
    expect(removeSkill(promoted, 's-5').filter((row) => row.isPrimary)).toHaveLength(1);
    expect(removeSkill(removeSkill(promoted, 's-5'), 's-3')).toEqual([]);
    // Deactivating a skill in the catalog drops its row and promotes a replacement primary.
    const deactivated = dropInactive(rows, [{ id: 's-9', name: 'Zinc', isActive: false }]);
    expect(deactivated.map((row) => row.name)).toEqual(['Electrical', 'Alarms']);
    expect(deactivated.filter((row) => row.isPrimary).map((row) => row.name)).toEqual(['Alarms']);
  });

  it.each([
    [{ date: '', kind: null, start: null, end: null, reason: ' ' }, ['date', 'kind', 'reason']],
    [{ date: '2099-01-01', kind: 'partial', start: null, end: null, reason: 'x' }, ['start']],
    [{ date: '2099-01-01', kind: 'extended', start: '10:00', end: '09:00', reason: 'x' }, ['end']],
    [
      { date: '2000-01-01', kind: 'unavailable', start: null, end: null, reason: 'x'.repeat(201) },
      ['date', 'reason'],
    ],
    [{ date: '2099-01-01', kind: 'unavailable', start: null, end: null, reason: 'Out' }, []],
  ] as const)('validates exception %#', (value, expected) => {
    expect(Object.keys(validateException(value, '2026-10-01'))).toEqual(expected);
  });

  it('shows conflict text by backend code and never raw problem text', () => {
    expect(exceptionConflictMessage(api({ kind: 'conflict', code: 'exception_past' }), false)).toBe(
      "Past exceptions can't be changed.",
    );
    expect(exceptionConflictMessage(api({ kind: 'conflict' }), true)).toBe(
      'This technician already has an active exception on this date.',
    );
    expect(exceptionConflictMessage(api({ kind: 'conflict', message: 'generic' }), false)).toBe(
      'generic',
    );
  });
});

describe('Skills & availability page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<SkillsAvailabilityPage>;
  let host: HTMLElement;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(
      (element) => element.textContent?.trim() === label,
    );

  async function setup(role: string, respond?: () => void): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ technicianId: 't-1' })) },
        },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    call('GET', 'sessions/current').flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    fixture = TestBed.createComponent(SkillsAvailabilityPage);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    respond?.();
    await settle();
  }

  afterEach(() => httpTesting.verify());

  it.each(['accounting', 'viewer', 'unknown_role'])(
    'shows the forbidden state with no requests for %s',
    async (role) => {
      await setup(role);
      expect(text()).toContain("You don't have access to Team.");
      expect(button('Save changes')).toBeUndefined();
    },
  );

  it.each(['dispatcher', 'technician'])(
    '%s reads the page without edit controls or a catalog request',
    async (role) => {
      await setup(role, () => call('GET', PAGE).flush(DATA));
      expect(text()).toContain('Carlos Rivera');
      expect(text()).toContain('Home branch: Austin Central');
      expect(text()).toContain('Time zone: (UTC-05:00) Central Time');
      expect(text()).toContain('Plumbing');
      expect(text()).toContain('Sep 24, 2099');
      expect(text()).toContain('Available now');
      expect(text()).toContain('28 available hours per week');
      for (const label of [
        'Save changes',
        'Copy Monday to weekdays',
        'Reset',
        'Add exception',
        'Manage skill list',
      ]) {
        expect(button(label), label).toBeUndefined();
      }
    },
  );

  it('renders the editor view and navigation targets for an owner', async () => {
    await setup('owner', () => {
      call('GET', PAGE).flush(DATA);
      call('GET', 'team/skills').flush(CATALOG);
    });
    for (const label of [
      'Save changes',
      'Copy Monday to weekdays',
      'Reset',
      'Add exception',
      'Manage skill list',
    ]) {
      expect(button(label), label).toBeDefined();
    }
    expect(button('Save changes')?.disabled).toBe(true);
    const href = (label: string) =>
      Array.from(host.querySelectorAll<HTMLAnchorElement>('a'))
        .find((element) => element.textContent?.trim().startsWith(label))
        ?.getAttribute('href');
    expect(href('Team')).toBe('/team');
    expect(href('View profile')).toBe('/coming-soon/technician-profile');
    expect(href('Overview')).toBe('/coming-soon/technician-profile');
    expect(href('Schedule')).toBe('/coming-soon/schedule');
    expect(href('Job history')).toBe('/coming-soon/technician-job-history');
    expect(href('Open dispatch calendar')).toBe('/coming-soon/schedule');
    expect(text()).toContain('85%');
    expect(text()).toContain('FieldOps never assigns a technician automatically.');
  });

  it('saves once, keeps the form on conflict, reloads and guards leaving when dirty', async () => {
    await setup('owner', () => {
      call('GET', PAGE).flush(DATA);
      call('GET', 'team/skills').flush(CATALOG);
    });
    const page = fixture.componentInstance;
    const confirm = vi.spyOn(fixture.debugElement.injector.get(ConfirmationService), 'confirm');
    expect(page.canLeave()).toBe(true);

    // Turning Friday On (new day, 100%) makes the form dirty and valid.
    (
      page as unknown as {
        days: { update: (fn: (d: ReturnType<typeof toDayForms>) => unknown) => void };
      }
    ).days.update((days) =>
      days.map((item) => (item.dayOfWeek === 5 ? { ...item, on: true } : item)),
    );
    await settle();
    const save = button('Save changes')!;
    expect(save.disabled).toBe(false);

    const leaving = page.canLeave();
    expect(isObservable(leaving)).toBe(true);
    const left = firstValueFrom(leaving as Observable<boolean>);
    expect(confirm).toHaveBeenCalledOnce();
    expect(confirm.mock.calls[0][0].header).toBe('Discard unsaved changes?');
    confirm.mock.calls[0][0].accept?.();
    expect(await left).toBe(true);

    save.click();
    save.click();
    await settle();
    const put = call('PUT', PAGE);
    expect(put.request.body.version).toBe('v1');
    expect(
      put.request.body.weeklyAvailability.map((item: { dayOfWeek: number }) => item.dayOfWeek),
    ).toEqual([1, 2, 3, 4, 5]);
    expect(put.request.body.skills).toEqual([
      { skillId: 's-1', proficiency: 4, isPrimary: true },
      { skillId: 's-2', proficiency: 3, isPrimary: false },
    ]);
    expect(button('Save changes')?.disabled).toBe(true);
    put.flush({ title: 'conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text()).toContain(
      'This technician was updated by someone else. Reload to see the latest changes.',
    );
    expect(button('Save changes')?.disabled).toBe(true);

    button('Reload')!.click();
    await settle();
    call('GET', PAGE).flush({ ...DATA, version: 'v2' });
    await settle();
    expect(text()).not.toContain('Reload to see the latest changes.');

    // 400 field errors show under the fields and nothing else changes.
    (
      page as unknown as {
        days: { update: (fn: (d: ReturnType<typeof toDayForms>) => unknown) => void };
      }
    ).days.update((days) =>
      days.map((item) => (item.dayOfWeek === 6 ? { ...item, on: true } : item)),
    );
    await settle();
    button('Save changes')!.click();
    await settle();
    call('PUT', PAGE).flush(
      {
        errors: {
          'weeklyAvailability[6].start': ['Choose a start and end time.'],
          skills: ['Choose one primary skill.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Choose a start and end time.');
    expect(text()).toContain('Choose one primary skill.');
  });

  it('renders empty, error-with-retry and not-available states', async () => {
    await setup('owner', () => {
      call('GET', PAGE).flush(EMPTY);
      call('GET', 'team/skills').flush([]);
    });
    expect(text()).toContain('0 available hours per week');
    expect(text()).toContain('No skills assigned yet.');
    expect(text()).toContain('No upcoming exceptions.');
    expect(host.querySelector('.sa__stats')?.textContent?.replace(/\s+/g, ' ')).toContain(
      'Utilization —',
    );
    expect(host.querySelector('.sa__bar')).toBeNull();

    // A failed reload shows the page error with Retry, then a 404 shows the not-available state.
    fixture.componentInstance['load']();
    await settle();
    call('GET', PAGE).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load this technician's skills and availability.");
    button('Retry')!.click();
    await settle();
    call('GET', PAGE).flush({}, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This technician profile isn't available.");
    expect(host.querySelector('a[href="/team"]')).not.toBeNull();
  });
});
