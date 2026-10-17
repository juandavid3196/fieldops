import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { ScheduleResponse, ScheduleVisit } from '../../models/technician-visits.model';
import { TechnicianSchedule } from './schedule';

@Component({ selector: 'app-visit-stub', template: '' })
class VisitStub {}

const API = 'http://api.test';
const SCHEDULE_URL = `${API}/technician/schedule`;
const ZONE = 'Pacific/Auckland';

function week(weekStart: string, visits: ScheduleVisit[] = []): ScheduleResponse {
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = new Date(Date.parse(`${weekStart}T12:00:00Z`) + index * 86_400_000)
      .toISOString()
      .slice(0, 10);
    const own = visits.filter((v) => v.start.startsWith(date) && v.status !== 'cancelled');
    return {
      date,
      visitCount: own.length,
      scheduledMinutes: own.reduce((sum, v) => sum + v.durationMinutes, 0),
    };
  });
  return {
    timezone: ZONE,
    today: '2026-10-07',
    weekStart,
    days,
    visits,
    unresolvedVisits: [],
  };
}

function scheduled(id: string, start: string, overrides: Partial<ScheduleVisit> = {}) {
  return {
    visitId: id,
    displayNumber: `WO-${id}`,
    title: `Job ${id}`,
    status: 'assigned',
    start,
    end: start,
    durationMinutes: 90,
    customerName: 'Sofia Martinez',
    customerType: 'person',
    address: { line1: '1842 Oak Street', city: 'Austin', stateRegion: 'TX', postalCode: '78704' },
    needsReschedule: false,
    ...overrides,
  } satisfies ScheduleVisit;
}

/** Text of the node with a space between elements (Angular drops whitespace-only text nodes). */
function spaced(node: Element | null | undefined): string {
  const parts: string[] = [];
  const walker = document.createTreeWalker(node as Node, NodeFilter.SHOW_TEXT);
  for (let text = walker.nextNode(); text; text = walker.nextNode()) {
    parts.push(text.textContent ?? '');
  }
  return parts.join(' ').replace(/\s+/g, ' ').trim();
}

describe('TechnicianSchedule', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let root: HTMLElement;

  async function open(roleCode = 'technician'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'today/schedule', component: TechnicianSchedule },
          { path: 'today/visits/:id', component: VisitStub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: {
        id: 'u-1',
        firstName: 'Carlos',
        lastName: 'Rivera',
        email: 'c@example.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: 'Role' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today/schedule');
    root = harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  const flat = spaced;
  const initial = () =>
    httpTesting.expectOne((r) => r.url === SCHEDULE_URL && !r.params.has('weekStart'));
  const weekRequest = (weekStart: string) =>
    httpTesting.expectOne((r) => r.url === SCHEDULE_URL && r.params.get('weekStart') === weekStart);
  const click = async (element: Element | null | undefined) => {
    (element as HTMLElement).click();
    await settle();
  };
  const days = () => Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));
  const selectedDay = () => flat(root.querySelector('[role="tab"][aria-selected="true"]'));
  const control = (label: string) => root.querySelector(`button[aria-label="${label}"]`);

  afterEach(() => httpTesting.verify());

  it('renders the week strip, day summary, timeline cards in the response zone and changes week and day (AC-03, AC-17, AC-19)', async () => {
    await open();
    expect(root.querySelector('[aria-busy="true"]')).not.toBeNull();
    initial().flush(
      week('2026-10-05', [
        scheduled('1', '2026-10-07T08:00:00+13:00', {
          customerType: 'company',
          needsReschedule: true,
        }),
        scheduled('2', '2026-10-07T14:30:00+13:00', { durationMinutes: 45 }),
        scheduled('3', '2026-10-07T16:00:00+13:00', { status: 'cancelled', durationMinutes: 120 }),
        scheduled('4', '2026-10-08T09:00:00+13:00', { durationMinutes: 120 }),
      ]),
    );
    await settle();

    expect(flat(root.querySelector('h1'))).toBe('My schedule');
    expect(days().map((d) => flat(d))).toEqual([
      'Mon 5',
      'Tue 6',
      'Wed 7 Today',
      'Thu 8',
      'Fri 9',
      'Sat 10',
      'Sun 11',
    ]);
    expect(selectedDay()).toBe('Wed 7 Today');
    expect(flat(root.querySelector('h2'))).toBe('Wednesday, Oct 7');
    expect(flat(root.querySelector('.sched__chip'))).toBe('2 jobs · 2.3h');
    expect(root.querySelectorAll('ol[aria-labelledby] > li')).toHaveLength(3);
    const first = root.querySelector('ol > li')!;
    expect(flat(first)).toContain('8:00 AM');
    expect(flat(first)).toContain('Scheduled 1h 30m Job 1 #WO-1');
    expect(flat(first)).toContain('1842 Oak Street Austin, TX 78704');
    expect(flat(first)).toContain('Ask dispatch to reschedule');
    expect(first.querySelector('[data-icon="building"]')).not.toBeNull();
    expect(first.querySelector('a')?.getAttribute('href')).toBe('/today/visits/1');
    expect(flat(root.querySelectorAll('ol > li')[1])).toContain('45m');
    expect(flat(root.querySelectorAll('ol > li')[2])).toContain('Cancelled 2h');
    expect(root.querySelectorAll('.sched__card--cancelled')).toHaveLength(1);
    expect(flat(root.querySelector('.sched__notice'))).toBe(
      'Your schedule is shown in your branch time zone.',
    );

    // Any day is selectable; an empty day keeps navigation enabled.
    await click(days()[0]);
    expect(flat(root.querySelector('h2'))).toBe('Monday, Oct 5');
    expect(flat(root.querySelector('.sched__chip'))).toBe('0 jobs');
    expect(flat(root.querySelector('.sched__empty'))).toBe('No jobs scheduled for this day.');
    expect(control('Next week')?.getAttribute('aria-disabled')).not.toBe('true');

    // Next week selects the same weekday (Monday); a failure keeps the prior week and selection.
    await click(control('Next week'));
    weekRequest('2026-10-12').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(flat(root.querySelector('p-message'))).toContain(
      "We couldn't load your schedule. Check your connection and try again.",
    );
    expect(selectedDay()).toBe('Mon 5');
    expect(flat(root.querySelector('h2'))).toBe('Monday, Oct 5');

    await click(Array.from(root.querySelectorAll('p-message button')).at(-1));
    weekRequest('2026-10-12').flush(week('2026-10-12'));
    await settle();
    expect(root.querySelector('p-message')).toBeNull();
    expect(days().map((d) => flat(d))[0]).toBe('Mon 12');
    expect(selectedDay()).toBe('Mon 12');

    // One request at a time: a repeated click is ignored and a replaced week change is cancelled.
    await click(control('Previous week'));
    await click(control('Previous week'));
    const replaced = weekRequest('2026-10-05');
    await click(control('Next week'));
    expect(replaced.cancelled).toBe(true);
    weekRequest('2026-10-19').flush(week('2026-10-19'));
    await settle();
    expect(days().map((d) => flat(d))[0]).toBe('Mon 19');
    expect(selectedDay()).toBe('Mon 19');
  });

  it('shows the load error with Retry and the access state like Today (AC-03, AC-17)', async () => {
    await open();
    initial().flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(flat(root)).toContain(
      "We couldn't load your schedule. Check your connection and try again.",
    );
    await click(root.querySelector('p-message button'));
    initial().flush(week('2026-10-05'));
    await settle();
    expect(root.querySelector('app-unresolved-jobs')).toBeNull();
    expect(root.querySelector('app-unresolved-banner')).toBeNull();

    TestBed.resetTestingModule();
    await open('office_manager');
    httpTesting.expectNone((r) => r.url === SCHEDULE_URL);
    expect(flat(root.querySelector('[role="status"]'))).toBe(
      "You don't have access to Today's jobs.",
    );
  });

  it('opens the Unfinished jobs sheet oldest first and resolves jobs with Open active visit and Resume job (AC-07, AC-20)', async () => {
    const since = (ms: number) => new Date(Date.now() - ms).toISOString();
    await open();
    initial().flush({
      ...week('2026-10-05'),
      unresolvedVisits: [
        {
          visitId: 'v-1',
          displayNumber: 'WO-1',
          title: 'Plumbing leak repair',
          status: 'in_progress',
          scheduledStart: '2026-10-04T09:00:00+13:00',
          customerName: 'Sofia Martinez',
          customerType: 'person',
          statusSince: since(2 * 86_400_000 + 60_000),
        },
        {
          visitId: 'v-2',
          displayNumber: 'WO-2',
          title: 'HVAC maintenance',
          status: 'paused',
          scheduledStart: null,
          customerName: 'Greenway Offices',
          customerType: 'company',
          // Older API: field absent entirely.
        },
        {
          visitId: 'v-3',
          displayNumber: 'WO-3',
          title: 'Boiler check',
          status: 'in_progress',
          scheduledStart: null,
          customerType: 'person',
          statusSince: 'not-a-date',
        },
      ],
    } as never);
    await settle();

    const block = root.querySelector<HTMLButtonElement>('app-unresolved-jobs button.jobs__block')!;
    expect(flat(block)).toBe('Unfinished jobs 3 jobs from previous days 3');
    expect(root.querySelector('app-unresolved-banner')).toBeNull();
    expect(
      block.compareDocumentPosition(root.querySelector('[role="tabpanel"]')!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      root.querySelector('[role="tablist"]')!.compareDocumentPosition(block) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    block.focus();
    await click(block);
    const dialog = document.querySelector('[role="dialog"]')!;
    const cards = Array.from(dialog.querySelectorAll('li'));
    expect(flat(dialog)).toContain('Finish or close earlier visits before starting new work.');
    expect(flat(dialog)).toContain('Oldest unfinished job shown first.');
    expect(cards).toHaveLength(3);
    expect(flat(cards[0])).toBe(
      'In progress Sun, Oct 4 Plumbing leak repair #WO-1 Sofia Martinez Started 2 days ago Open active visit',
    );
    expect(flat(cards[1])).toBe('Paused HVAC maintenance #WO-2 Greenway Offices Resume job');
    // Missing statusSince / customerName: no "NaN" line, no orphan icon.
    expect(flat(cards[2])).toBe('In progress Boiler check #WO-3 Open active visit');
    expect(cards[2].querySelector('.jobs__icon')).toBeNull();
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('/today/visits/v-1');
    expect(cards[1].querySelector('[data-icon="building"]')).not.toBeNull();

    // Resume failure: inline error, sheet and action kept; a repeat while pending is ignored.
    const resume = () =>
      Array.from(dialog.querySelectorAll('button')).find((b) => flat(b) === 'Resume job')!;
    await click(resume());
    await click(resume());
    httpTesting
      .expectOne({ method: 'POST', url: `${API}/technician/visits/v-2/resume` })
      .flush(
        { status: 409, code: 'visit_status_invalid' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    expect(flat(cards[1])).toContain("This job can't be resumed in its current state.");
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(flat(resume())).toBe('Resume job');

    // Escape closes the sheet and returns focus to the block.
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    await settle();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(block);

    // Resume success closes the sheet and opens the visit.
    await click(block);
    const again = Array.from(document.querySelectorAll('[role="dialog"] button')).find(
      (b) => flat(b) === 'Resume job',
    );
    await click(again);
    httpTesting.expectOne({ method: 'POST', url: `${API}/technician/visits/v-2/resume` }).flush({});
    await settle();
    await settle();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/today/visits/v-2');
  });
});
