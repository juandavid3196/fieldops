import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { BusinessHoursBody, BusinessHoursDay } from '../../models/business-hours.model';
import { BusinessHours } from './business-hours';

const API = 'http://api.test';
const BRANCHES_URL = `${API}/branches`;
const hoursUrl = (id: string): string => `${API}/branches/${id}/business-hours`;

const branch = (id: string, name: string, extra: Record<string, unknown> = {}) => ({
  id,
  name,
  code: id.toUpperCase(),
  timezone: 'America/Bogota',
  isActive: true,
  isMain: false,
  ...extra,
});
const MAIN = branch('b1', 'Main branch', { isMain: true });
const ANNEX = branch('b2', 'Annex', { isActive: false });

const DAY_NAMES = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
] as const;

function body(
  overrides: Partial<BusinessHoursBody> = {},
  days: Record<string, [string, string] | null> = {},
): BusinessHoursBody {
  const list: BusinessHoursDay[] = DAY_NAMES.map((day) => {
    const times = day in days ? days[day] : day === 'sunday' ? null : ['08:00', '17:00'];
    return {
      dayOfWeek: day,
      isOpen: times !== null,
      openingTime: times?.[0] ?? null,
      closingTime: times?.[1] ?? null,
    };
  });
  return {
    branchId: 'b1',
    branchName: 'Main branch',
    timezone: 'America/Bogota',
    updatedAt: '2026-10-20T10:00:00.000000Z',
    canManage: true,
    days: list,
    ...overrides,
  };
}

@Component({ template: '<p>Other page</p>' })
class OtherStub {}

@Component({ template: '<p>Sign in</p>' })
class SignInStub {}

describe('BusinessHours page', { timeout: 20_000 }, () => {
  let httpTesting: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;
  let host: HTMLElement;

  async function setup(): Promise<void> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'admin/business-hours',
            component: BusinessHours,
            canDeactivate: [(page: BusinessHours) => page.canLeave()],
          },
          { path: 'other', component: OtherStub },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/business-hours', BusinessHours);
    host = harness.routeNativeElement as HTMLElement;
  }

  const stable = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  // A navigation held by the guard keeps `whenStable` pending, so wait on a timer instead.
  const pendingNavigation = async (): Promise<void> => {
    await new Promise((resolve) => setTimeout(resolve, 50));
    harness.detectChanges();
  };
  const page = (): BusinessHours => harness.routeDebugElement!.componentInstance as BusinessHours;
  const text = (): string => document.body.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const dialogButton = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')).find(
      (b) => b.textContent?.trim() === label,
    );
  const fail = (url: string, status: number, problem: object | null = null): void =>
    httpTesting.expectOne(url).flush(problem, { status, statusText: 'Error' });

  async function load(branches: object[], hours: BusinessHoursBody = body()): Promise<void> {
    await setup();
    expect(host.querySelectorAll('[aria-busy="true"]').length).toBeGreaterThan(0);
    httpTesting.expectOne(BRANCHES_URL).flush({ items: branches });
    await stable();
    httpTesting.expectOne(hoursUrl(hours.branchId)).flush(hours);
    await stable();
  }

  afterEach(() => httpTesting.verify());

  it.each([
    ['branches fail', "We couldn't load branches.", 'branches'],
    ['hours fail', "We couldn't load business hours.", 'hours'],
    ['no branches', 'No branches available.', 'empty'],
    ['forbidden', "You don't have access to business hours.", 'forbidden'],
  ])(
    'renders the %s state with Retry where applicable (AC-12, AC-13)',
    async (_name, message, kind) => {
      await setup();
      if (kind === 'forbidden') {
        fail(BRANCHES_URL, 403);
      } else if (kind === 'empty') {
        httpTesting.expectOne(BRANCHES_URL).flush({ items: [] });
      } else if (kind === 'branches') {
        fail(BRANCHES_URL, 500);
      } else {
        httpTesting.expectOne(BRANCHES_URL).flush({ items: [MAIN] });
        await stable();
        fail(hoursUrl('b1'), 500);
      }
      await stable();
      expect(text()).toContain(message);
      expect(host.querySelector('app-administration-nav') !== null).toBe(kind !== 'forbidden');

      if (kind === 'branches' || kind === 'hours') {
        expect(button('Retry')).toBeDefined();
        button('Retry')!.click();
        await stable();
        if (kind === 'branches') {
          httpTesting.expectOne(BRANCHES_URL).flush({ items: [MAIN] });
          await stable();
        }
        httpTesting.expectOne(hoursUrl('b1')).flush(body());
        await stable();
        expect(text()).toContain('Weekly hours');
      }
    },
  );

  it('selects the main branch, labels inactive ones and shows a view-only page (AC-11, AC-13, AC-01)', async () => {
    await load([ANNEX, MAIN], body({ canManage: false }));

    expect(
      page()
        .branchOptions()
        .map((o) => o.label),
    ).toEqual(['Annex (Inactive)', 'Main branch']);
    expect(page().selectedBranchId()).toBe('b1');
    expect(host.querySelector<HTMLInputElement>('#business-hours-timezone')!.value).toBe(
      'America/Bogota (GMT-5)',
    );
    expect(text()).toContain('You have view-only access to business hours.');
    expect(button('Save changes')).toBeUndefined();
    expect(button('Cancel')).toBeUndefined();
    expect(page().locked()).toBe(true);
    expect(host.querySelectorAll('p-toggleswitch input:disabled')).toHaveLength(7);
    expect(text()).toContain('Business hours follow the selected branch time zone.');

    // No time zone shows "Not set".
    page().body.set({ ...body(), timezone: null });
    await stable();
    expect(host.querySelector<HTMLInputElement>('#business-hours-timezone')!.value).toBe('Not set');
  });

  it('edits days, validates inline, never sends closed times and adopts the saved values (AC-14..AC-16, AC-19)', async () => {
    await load([MAIN], body({}, { monday: ['08:15', '17:00'] }));

    const mondayOpening = page().options('monday', 'openingTime');
    expect(mondayOpening[0].label).toBe('12:00 AM');
    expect(mondayOpening.at(-1)!.label).toBe('11:30 PM');
    expect(mondayOpening.some((o) => o.value === '08:15' && o.label === '8:15 AM')).toBe(true);
    expect(page().options('monday', 'closingTime')[0].label).toBe('12:30 AM');
    expect(text()).toContain('No unsaved changes');
    expect(button('Save changes')!.disabled).toBe(true);

    // Reopening a closed day proposes 8:00 AM-5:00 PM; closing it again is clean.
    page().setOpen('sunday', true);
    expect(page().rows()[6]).toMatchObject({ isOpen: true, opening: '08:00', closing: '17:00' });
    expect(page().dirty()).toBe(true);
    page().setOpen('sunday', false);
    expect(page().dirty()).toBe(false);

    // Client validation sends nothing and focuses the first invalid field.
    page().setOpen('wednesday', false);
    page().setTime('friday', 'closingTime', '07:00');
    page().setTime('tuesday', 'openingTime', null);
    await stable();
    expect(text()).toContain('Unsaved changes');
    page().save();
    await stable();
    expect(page().error('tuesday', 'openingTime')).toBe('This field is required.');
    expect(page().error('friday', 'closingTime')).toBe('Closing time must be after opening time.');
    expect(host.textContent).toContain('Closing time must be after opening time.');
    expect(document.activeElement?.id).toBe('business-hours-tuesday-openingTime');

    page().setTime('tuesday', 'openingTime', '08:00');
    page().setTime('friday', 'closingTime', '17:00');
    page().save();
    await stable();
    const request = httpTesting.expectOne({ method: 'PUT', url: hoursUrl('b1') });
    expect(page().submitting()).toBe(true);
    expect(page().locked()).toBe(true);
    expect(request.request.body.updatedAt).toBe('2026-10-20T10:00:00.000000Z');
    expect(request.request.body.days).toEqual([
      { dayOfWeek: 'monday', isOpen: true, openingTime: '08:15', closingTime: '17:00' },
      { dayOfWeek: 'tuesday', isOpen: true, openingTime: '08:00', closingTime: '17:00' },
      { dayOfWeek: 'wednesday', isOpen: false, openingTime: null, closingTime: null },
      { dayOfWeek: 'thursday', isOpen: true, openingTime: '08:00', closingTime: '17:00' },
      { dayOfWeek: 'friday', isOpen: true, openingTime: '08:00', closingTime: '17:00' },
      { dayOfWeek: 'saturday', isOpen: true, openingTime: '08:00', closingTime: '17:00' },
      { dayOfWeek: 'sunday', isOpen: false, openingTime: null, closingTime: null },
    ]);
    request.flush(
      body(
        { updatedAt: '2026-10-20T11:00:00.000000Z' },
        { monday: ['08:15', '17:00'], wednesday: null },
      ),
    );
    await stable();

    expect(page().submitting()).toBe(false);
    expect(page().dirty()).toBe(false);
    expect(text()).toContain('Business hours saved for Main branch');
    expect(text()).toContain('No unsaved changes');
  });

  it('restores on Cancel and confirms discarding on branch switch and route leave (AC-17, AC-18)', async () => {
    await load([MAIN, ANNEX]);

    page().setTime('monday', 'closingTime', '18:00');
    await stable();
    expect(button('Cancel')!.disabled).toBe(false);
    button('Cancel')!.click();
    await stable();
    expect(page().dirty()).toBe(false);
    expect(page().rows()[0].closing).toBe('17:00');

    // Branch switch: Keep editing reverts the selector; Discard loads the other branch.
    page().setTime('monday', 'closingTime', '18:00');
    page().onBranchChange('b2');
    await stable();
    dialogButton('Keep editing')!.click();
    await stable();
    expect(page().branchControl.value).toBe('b1');
    expect(page().dirty()).toBe(true);
    page().branchControl.setValue('b2', { emitEvent: false });
    page().onBranchChange('b2');
    await stable();
    dialogButton('Discard changes')!.click();
    await stable();
    httpTesting.expectOne(hoursUrl('b2')).flush(body({ branchId: 'b2', branchName: 'Annex' }));
    await stable();
    expect(page().selectedBranchId()).toBe('b2');
    expect(page().dirty()).toBe(false);

    // Route leave: Keep editing stays, Discard completes the navigation.
    page().setTime('monday', 'closingTime', '18:00');
    const leaving = router.navigateByUrl('/other');
    await pendingNavigation();
    dialogButton('Keep editing')!.click();
    expect(await leaving).toBe(false);
    expect(router.url).toBe('/admin/business-hours');
    const again = router.navigateByUrl('/other');
    await pendingNavigation();
    dialogButton('Discard changes')!.click();
    expect(await again).toBe(true);
    expect(router.url).toBe('/other');
  });

  it('handles stale 409 with Reload, server field errors and a 404 branch (AC-15, AC-20)', async () => {
    await load([MAIN]);

    page().setTime('monday', 'closingTime', '18:00');
    page().save();
    httpTesting.expectOne({ method: 'PUT', url: hoursUrl('b1') }).flush(null, {
      status: 409,
      statusText: 'Conflict',
    });
    await stable();
    expect(text()).toContain(
      'This record was changed by someone else. Reload to see the latest version.',
    );
    expect(page().dirty()).toBe(true);
    button('Reload')!.click();
    await stable();
    httpTesting.expectOne(hoursUrl('b1')).flush(body());
    await stable();
    expect(page().dirty()).toBe(false);
    expect(button('Reload')).toBeUndefined();

    page().setTime('monday', 'closingTime', '18:00');
    page().save();
    httpTesting
      .expectOne({ method: 'PUT', url: hoursUrl('b1') })
      .flush(
        { errors: { 'days.monday.closingTime': ['Closing time must be after opening time.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await stable();
    expect(page().error('monday', 'closingTime')).toBe('Closing time must be after opening time.');
    expect(page().dirty()).toBe(true);

    page().save();
    httpTesting.expectOne({ method: 'PUT', url: hoursUrl('b1') }).flush(null, {
      status: 404,
      statusText: 'Not Found',
    });
    await stable();
    httpTesting.expectOne(BRANCHES_URL).flush({ items: [branch('b3', 'North', { isMain: true })] });
    await stable();
    httpTesting.expectOne(hoursUrl('b3')).flush(body({ branchId: 'b3', branchName: 'North' }));
    await stable();
    expect(text()).toContain('This branch is no longer available.');
    expect(page().selectedBranchId()).toBe('b3');
    expect(page().dirty()).toBe(false);
  });
});
