import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  TechnicianVisitDetail,
  TodayVisitStatus,
  TravelResult,
} from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { VisitReview } from '../visit-review/visit-review';
import { VisitDetail } from './visit-detail';

const API = 'http://api.test';
const URL = `${API}/technician/visits/v-1`;

const DETAIL: TechnicianVisitDetail = {
  visitId: 'v-1',
  visitNumber: 2,
  workOrderId: 'wo-1',
  displayNumber: 'WO-3091',
  title: 'Kitchen sink leak repair',
  status: 'assigned',
  start: '2026-10-07T09:30:00+13:00',
  end: '2026-10-07T11:30:00+13:00',
  arrivalWindowStart: '2026-10-07T09:30:00+13:00',
  arrivalWindowEnd: '2026-10-07T10:30:00+13:00',
  priority: 2,
  serviceCategory: 'Plumbing',
  customerName: 'Sofia Martinez',
  phone: '(512) 555-0100',
  address: {
    line1: '1842 Oak Street',
    line2: null,
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78704',
    countryCode: 'US',
  },
  latitude: 30.25,
  longitude: -97.75,
  plannedMaterialsCount: 4,
  isPrimary: true,
  date: '2026-10-07',
  timezone: 'Pacific/Auckland',
  dispatchNote: 'Gate code 4411',
  customerType: 'company',
  access: {
    instructions: 'Use front entrance.',
    contactPreference: 'email_or_sms',
    activeDamage: true,
  },
  instructions: 'Inspect cabinet before beginning.',
  scope: '- Replace P-trap\n* Install shutoff valve\n\n• Test drainage\nClean work area',
  requiredSkills: ['Leak repair', 'Plumbing'],
  officePhone: '(512) 555-0199',
  travel: { startedAt: null, arrivedAt: null, durationMinutes: null },
  plannedMaterials: [
    {
      id: 'm-1',
      description: 'P-trap assembly',
      quantity: 1,
      unit: 'each',
      source: 'truck_stock',
      usedQuantity: 0,
    },
    {
      id: 'm-2',
      description: 'Shutoff valve',
      quantity: 2,
      unit: 'each',
      source: 'warehouse',
      usedQuantity: 0,
    },
    {
      id: 'm-3',
      description: 'Plumbers tape',
      quantity: 1,
      unit: 'roll',
      source: 'to_purchase',
      usedQuantity: 0,
    },
    {
      id: 'm-4',
      description: 'Silicone',
      quantity: 1,
      unit: 'tube',
      source: 'truck_stock',
      usedQuantity: 0,
    },
  ],
  tasks: [
    {
      id: 't-1',
      label: 'Photograph repair',
      isRequired: true,
      isCompleted: true,
      notes: null,
      completedAt: '2026-10-07T09:50:00+13:00',
    },
    {
      id: 't-2',
      label: 'Clean up',
      isRequired: false,
      isCompleted: false,
      notes: null,
      completedAt: null,
    },
  ],
  assessment: {
    completedAt: '2026-10-05T10:00:00+13:00',
    diagnosis: 'Corroded P-trap',
    recommendedScope: 'Replace trap and valve',
    photos: [{ id: 'p-1' }, { id: 'p-2' }],
  },
  actualStartedAt: null,
  time: { workSeconds: 0, pauseSeconds: 0, activeEntry: null, estimatedMinutes: null },
  additionalMaterials: [],
  evidence: [],
  technicianNotes: null,
};

const MINIMAL: TechnicianVisitDetail = {
  ...DETAIL,
  arrivalWindowStart: null,
  arrivalWindowEnd: null,
  phone: null,
  latitude: null,
  longitude: null,
  plannedMaterialsCount: 0,
  dispatchNote: null,
  customerType: 'person',
  access: { instructions: null, contactPreference: null, activeDamage: false },
  instructions: null,
  scope: '',
  requiredSkills: [],
  officePhone: null,
  plannedMaterials: [],
  tasks: [],
  assessment: null,
};

const STARTED = '2026-10-07T09:02:00+13:00';
const ARRIVED = '2026-10-07T09:24:00+13:00';

function visit(
  status: TodayVisitStatus,
  overrides: Partial<TechnicianVisitDetail> = {},
): TechnicianVisitDetail {
  return { ...DETAIL, status, ...overrides };
}

const arrivedTravel = { startedAt: STARTED, arrivedAt: ARRIVED, durationMinutes: 22 };
const openTravel = { startedAt: STARTED, arrivedAt: null, durationMinutes: null };

describe('VisitDetail', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  async function open(body?: TechnicianVisitDetail, roleCode = 'technician'): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'today/visits/:visitId', component: VisitDetail },
          { path: 'today/visits/:visitId/review', component: VisitReview },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: { id: 'u-1', firstName: 'Carlos', lastName: 'Rivera', email: 'c@example.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: 'Role' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today/visits/v-1');
    if (body) {
      httpTesting.expectOne({ method: 'GET', url: URL }).flush(body);
      await settle();
    }
    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    harness.fixture.detectChanges();
  }

  const flat = (node: Element | null | undefined) =>
    (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const body = () => flat(document.body);
  const byText = (selector: string, label: string, scope: ParentNode = document) =>
    Array.from(scope.querySelectorAll<HTMLElement>(selector)).find((n) => flat(n).includes(label));
  const button = (label: string, scope: ParentNode = document) =>
    byText('button', label, scope) as HTMLButtonElement | undefined;
  const bar = (root: HTMLElement) => root.querySelector('.bar')!;
  const clickAndSettle = async (element: HTMLElement | undefined) => {
    element!.click();
    await settle();
  };

  // PrimeNG Tabs observe their list size; the test DOM has no ResizeObserver.
  beforeEach(() => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe = vi.fn();
        unobserve = vi.fn();
        disconnect = vi.fn();
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    httpTesting.verify();
  });

  it.each<[string, TechnicianVisitDetail, string[], string | null]>([
    ['assigned', visit('assigned'), ['current', 'upcoming', 'upcoming', 'upcoming'], null],
    [
      'on the way',
      visit('on_the_way', { travel: openTravel }),
      ['done', 'current', 'upcoming', 'upcoming'],
      'On the way • Started 9:02 AM',
    ],
    [
      'arrived',
      visit('on_the_way', { travel: arrivedTravel }),
      ['done', 'done', 'current', 'upcoming'],
      'Arrived • 9:24 AM Travel time 22 min',
    ],
    ['in progress', visit('in_progress'), ['done', 'done', 'done', 'current'], null],
    ['completed', visit('completed'), ['done', 'done', 'done', 'done'], 'Completed'],
  ])(
    'shows the stepper states as text and the status banner for %s (AC-14)',
    async (_name, detail, states, banner) => {
      const root = await open(detail);

      expect(
        Array.from(root.querySelectorAll('.stepper__step'), (step) => [
          step.querySelector('.stepper__label')?.textContent,
          step.querySelector('.stepper__state')?.textContent,
        ]),
      ).toEqual(
        ['Scheduled', 'On the way', 'Arrived', 'In progress'].map((label, index) => [
          label,
          `(${states[index]})`,
        ]),
      );
      const shown = root.querySelector('.visit__banner');
      expect(shown === null ? null : flat(shown)).toBe(banner);
    },
  );

  it.each<[string, TechnicianVisitDetail]>([
    ['a complete visit', DETAIL],
    ['a minimal visit', MINIMAL],
  ])(
    'renders the header, access card and read-only tabs for %s (AC-05, AC-17, AC-19)',
    async (name, detail) => {
      const root = await open(detail);
      const rich = detail === DETAIL;
      const text = flat(root);

      expect(root.querySelector('h1')?.textContent).toBe('Kitchen sink leak repair');
      for (const expected of [
        'High priority',
        '#WO-3091',
        'Sofia Martinez',
        '9:30 AM – 11:30 AM',
      ]) {
        expect(text).toContain(expected);
      }
      expect(text).toContain('1842 Oak Street, Austin, TX 78704');
      expect(text).not.toMatch(/Message|Live|Estimated|On time|Ready|mi\b/);
      expect(root.querySelector('[aria-label*="menu"]')).toBeNull();
      const directions = root.querySelector<HTMLAnchorElement>('a[target="_blank"]')!;
      expect(directions.textContent).toContain('Open directions');
      expect(directions.getAttribute('aria-label')).toContain('opens in a new tab');
      expect(directions.getAttribute('href')).toBe(
        rich
          ? 'https://www.google.com/maps/dir/?api=1&destination=30.25,-97.75'
          : `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent('1842 Oak Street, Austin, TX 78704')}`,
      );
      expect(root.querySelector('a[href^="tel:"]')?.getAttribute('href') ?? null).toBe(
        rich ? 'tel:5125550100' : null,
      );

      const access = flat(root.querySelectorAll('.visit__card')[1]);
      const tabs = (label: string) => byText('[role="tab"]', label, root)!;
      const panel = () => flat(root.querySelector('[role="tabpanel"]:not([hidden])'));
      expect(access).toContain(rich ? 'Commercial' : 'Residential');
      expect(
        access.includes('Active damage reported by the customer. Check the site before starting.'),
      ).toBe(rich);
      expect(access.includes('Contact preference: Email or text message')).toBe(rich);
      expect(access.includes('Use front entrance.')).toBe(rich);
      expect(access.includes('No access details provided.')).toBe(!rich);

      expect(root.querySelectorAll('[role="tab"]')).toHaveLength(4);
      expect(tabs('Overview').getAttribute('aria-selected')).toBe('true');
      const overview = panel();
      expect(overview.includes('Technician instructions')).toBe(rich);
      expect(overview.includes('Scope of work')).toBe(rich);
      expect(overview.includes('Required skills')).toBe(rich);
      expect(overview.includes('View approved assessment')).toBe(rich);
      expect(overview.includes('Planned materials')).toBe(rich);
      expect(overview.includes('Assessment photos')).toBe(rich);
      if (rich) {
        for (const part of [
          'Inspect cabinet before beginning.',
          'Dispatch note',
          'Gate code 4411',
        ]) {
          expect(overview).toContain(part);
        }
        expect(
          Array.from(root.querySelectorAll('.tabs__list li'), (li) => li.textContent?.trim()),
        ).toEqual(['Replace P-trap', 'Install shutoff valve', 'Test drainage', 'Clean work area']);
        expect(
          Array.from(root.querySelectorAll('.tabs__chips li'), (chip) => chip.textContent),
        ).toEqual(['Leak repair', 'Plumbing']);
        expect(overview).toContain('Assessment photos (2)');
        expect(overview).toContain('4 planned materials');
        for (const part of ['P-trap assembly · 1 each', 'Truck stock', 'Warehouse']) {
          expect(overview).toContain(part);
        }
        expect(overview).toContain('Plumbers tape · 1 roll');
        expect(overview).toContain('To purchase');
        expect(overview).not.toContain('Silicone');
        expect(overview).toContain('Confirm quantities after starting the job.');
        expect(
          Array.from(root.querySelectorAll('[role="tabpanel"]:not([hidden]) img'), (img) => [
            img.getAttribute('alt'),
            img.getAttribute('src'),
          ]),
        ).toEqual([
          ['Assessment photo 1 of 2', `${URL}/assessment-photos/p-1`],
          ['Assessment photo 2 of 2', `${URL}/assessment-photos/p-2`],
        ]);
      }

      await clickAndSettle(tabs('Tasks'));
      if (rich) {
        expect(panel()).toContain('1 of 2 tasks');
        expect(panel()).toContain('Photograph repair');
        expect(panel()).toContain('Required');
        expect(panel()).toContain('Tasks can be completed after starting the job.');
        const boxes = Array.from(
          root.querySelectorAll<HTMLInputElement>('[role="tabpanel"]:not([hidden]) input'),
        );
        expect(boxes.map((box) => [box.disabled, box.checked])).toEqual([
          [true, true],
          [true, false],
        ]);
      } else {
        expect(panel()).toBe('No tasks for this job.');
      }
      await clickAndSettle(tabs('Materials'));
      expect(panel()).toContain(rich ? 'Silicone · 1 tube' : 'No planned materials.');
      await clickAndSettle(tabs('Photos'));
      expect(panel() === 'No assessment photos.').toBe(!rich);
      expect(name).toBeTruthy();
    },
  );

  it.each<[string, TechnicianVisitDetail, string[], string, boolean]>([
    [
      'primary, assigned',
      visit('assigned'),
      ['Start travel', 'Start job'],
      'Available after arrival is recorded',
      false,
    ],
    [
      'primary, on the way',
      visit('on_the_way', { travel: openTravel }),
      ["I've arrived", 'Start job'],
      'Available after arrival is recorded',
      false,
    ],
    [
      'primary, arrived',
      visit('on_the_way', { travel: arrivedTravel }),
      ['Arrived 9:24 AM', 'Start job'],
      'Arrived 9:24 AM',
      true,
    ],
    [
      'non-primary, assigned',
      visit('assigned', { isPrimary: false }),
      ['Start job'],
      'The primary technician manages travel for this job.|Available after arrival is recorded',
      false,
    ],
    [
      'non-primary, arrived',
      visit('on_the_way', { travel: arrivedTravel, isPrimary: false }),
      [],
      'The primary technician manages travel for this job.',
      false,
    ],
  ])(
    'shows the action bar controls and helper texts for %s (AC-03, AC-15, AC-16)',
    async (_name, detail, buttons, texts, startEnabled) => {
      const root = await open(detail);
      const actions = bar(root);

      expect(
        Array.from(actions.querySelectorAll('button:not(.bar__report)'), (b) => flat(b)),
      ).toEqual(buttons);
      for (const text of texts.split('|')) {
        expect(flat(actions)).toContain(text);
      }
      expect(flat(actions)).not.toContain('Starting the job will be available soon.');
      expect(button('Start job', actions)?.disabled ?? true).toBe(!startEnabled);
      expect(button('Arrived', actions)?.disabled ?? true).toBe(true);
      expect(byText('button', 'Report delay or issue', actions)).toBeDefined();
    },
  );

  it('starts the job in place with pending protection, an announcement and a 409 reload (AC-16)', async () => {
    const root = await open(visit('on_the_way', { travel: arrivedTravel }));
    const start = () => button('Start job', bar(root));
    const working = visit('in_progress', {
      travel: arrivedTravel,
      actualStartedAt: '2026-10-07T09:31:00+13:00',
      evidence: [
        { id: 'e-1', type: 'before', createdAt: '2026-10-07T09:40:00+13:00' },
        { id: 'e-2', type: 'after', createdAt: '2026-10-07T10:40:00+13:00' },
      ],
    });

    start()!.click();
    start()!.click();
    await settle();
    expect(start()!.disabled).toBe(true);
    const request = httpTesting.expectOne({ method: 'POST', url: `${URL}/start-job` });
    expect(request.request.body).toBeNull();
    request.flush({ changed: true, visit: working } satisfies TravelResult);
    await settle();

    expect(root.querySelector('app-job-progress')).not.toBeNull();
    expect(root.querySelector('.visit__banner')).toBeNull();
    expect(flat(root.querySelector('.banner'))).toContain('In progress • ');
    expect(flat(root.querySelector('.banner'))).toContain('Started 9:31 AM');
    expect(root.querySelector('h1')?.textContent).toBe('Kitchen sink leak repair');
    expect(flat(root.querySelector('.visit__sr'))).toBe('Job started.');
    expect(flat(root.querySelector('.stepper'))).toContain('In progress(current)');
    expect(TestBed.inject(TechnicianVisitsService).pageTitle()).toBe('Job in progress');
    // Scheduled, On the way and Arrived are done: 3 of 4 steps.
    expect(TestBed.inject(TechnicianVisitsService).jobProgress()).toBe(75);
    expect(root.querySelector('a[href^="tel:"]')).toBeNull();
    expect(flat(root)).not.toMatch(/Access details|Message|\bmi\b/);

    // Job photos open in the viewer; Delete asks first, then replaces the visit and stays open.
    const thumbnail = root.querySelector<HTMLButtonElement>('.photos__button')!;
    thumbnail.focus();
    await clickAndSettle(thumbnail);
    expect(document.body.querySelector<HTMLImageElement>('.viewer__image')?.alt).toBe(
      'Before photo 1 of 1',
    );
    const remove = () => button('Delete photo', document.body);
    await clickAndSettle(remove());
    expect(body()).toContain('Delete this photo?');
    await clickAndSettle(button('Cancel', document.body));
    httpTesting.expectNone(`${URL}/evidence/e-1`);
    expect(document.activeElement).toBe(remove());
    await clickAndSettle(remove());
    const confirm = Array.from(document.querySelectorAll('.p-dialog')).find((dialog) =>
      flat(dialog).includes('Delete this photo?'),
    )!;
    await clickAndSettle(button('Delete', confirm));
    httpTesting
      .expectOne({ method: 'DELETE', url: `${URL}/evidence/e-1` })
      .flush({ ...working, evidence: [working.evidence[1]] });
    await settle();
    expect(document.body.querySelector<HTMLImageElement>('.viewer__image')?.alt).toBe(
      'After photo 1 of 1',
    );
    expect(flat(root.querySelector('.visit__sr'))).toBe('Photo deleted.');
    await clickAndSettle(
      document.body.querySelector<HTMLButtonElement>('p-dialog button[aria-label="Close"]')!,
    );

    // 409: the fixed message, then the visit reloads and keeps the page.
    TestBed.resetTestingModule();
    const again = await open(visit('on_the_way', { travel: arrivedTravel }));
    await clickAndSettle(button('Start job', bar(again)));
    httpTesting
      .expectOne({ method: 'POST', url: `${URL}/start-job` })
      .flush(
        { status: 409, code: 'visit_status_invalid' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    expect(flat(bar(again))).toContain("This job can't be started in its current state.");
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(visit('completed'));
    await settle();
    expect(flat(again.querySelector('.visit__banner'))).toBe('Completed');
  });

  it("runs Start travel and I've arrived in place with pending protection, announcements and error handling (AC-15)", async () => {
    const root = await open(visit('assigned'));
    const live = () => flat(root.querySelector('.visit__sr'));
    const control = (label: string) => button(label, bar(root));

    control('Start travel')!.click();
    control('Start travel')!.click();
    await settle();
    expect(control('Start travel')!.disabled).toBe(true);
    const start = httpTesting.expectOne({ method: 'POST', url: `${URL}/start-travel` });
    expect(start.request.body).toBeNull();
    start.flush({
      changed: true,
      visit: visit('on_the_way', { travel: openTravel }),
    } satisfies TravelResult);
    await settle();
    expect(flat(root.querySelector('.visit__banner'))).toBe('On the way • Started 9:02 AM');
    expect(live()).toBe('On the way. Started 9:02 AM.');
    expect(control("I've arrived")).toBeDefined();

    // 409: message from the code, then the visit reloads.
    await clickAndSettle(control("I've arrived"));
    httpTesting
      .expectOne({ method: 'POST', url: `${URL}/arrive` })
      .flush(
        { status: 409, code: 'visit_status_invalid' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    expect(flat(bar(root))).toContain(
      'Travel cannot be managed for this job in its current state.',
    );
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(visit('paused'));
    await settle();
    expect(root.querySelector('app-job-progress')).not.toBeNull();
    expect(flat(root.querySelector('.banner'))).toContain('Paused • 0m');
    expect(control("I've arrived")).toBeUndefined();

    // Another failure keeps the page and does not reload.
    TestBed.resetTestingModule();
    const again = await open(visit('on_the_way', { travel: openTravel }));
    await clickAndSettle(byText('button', "I've arrived", bar(again)));
    httpTesting.expectOne(`${URL}/arrive`).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(flat(bar(again))).toContain("We couldn't update this job. Try again.");
    expect(button("I've arrived", bar(again))?.disabled).toBe(false);

    await clickAndSettle(byText('button', "I've arrived", bar(again)));
    httpTesting.expectOne(`${URL}/arrive`).flush({
      changed: true,
      visit: visit('on_the_way', { travel: arrivedTravel }),
    } satisfies TravelResult);
    await settle();
    expect(flat(again.querySelector('.visit__sr'))).toBe('Arrived. 9:24 AM. Travel time 22 min.');
    expect(flat(again.querySelector('.visit__banner'))).toBe(
      'Arrived • 9:24 AM Travel time 22 min',
    );
    expect(button('Arrived 9:24 AM', bar(again))?.disabled).toBe(true);
  });

  it('opens the photo viewer, the assessment panel and the report panel, returning focus (AC-17, AC-18)', async () => {
    const root = await open(DETAIL);
    const thumbnails = () =>
      Array.from(root.querySelectorAll<HTMLButtonElement>('.photos__button'));

    // Viewer: alt text, previous/next with wrap-around, unavailable image, close returns focus.
    thumbnails()[1].focus();
    await clickAndSettle(thumbnails()[1]);
    const viewerImage = () => document.body.querySelector<HTMLImageElement>('.viewer__image');
    expect(viewerImage()?.alt).toBe('Assessment photo 2 of 2');
    await clickAndSettle(
      document.body.querySelector<HTMLButtonElement>('button[aria-label="Next photo"]')!,
    );
    expect(viewerImage()?.alt).toBe('Assessment photo 1 of 2');
    await clickAndSettle(
      document.body.querySelector<HTMLButtonElement>('button[aria-label="Previous photo"]')!,
    );
    expect(viewerImage()?.alt).toBe('Assessment photo 2 of 2');
    viewerImage()!.dispatchEvent(new Event('error'));
    await settle();
    expect(document.body.querySelector('.viewer')?.textContent).toContain('Photo unavailable.');
    await clickAndSettle(
      document.body.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!,
    );
    expect(document.body.querySelector('.viewer')).toBeNull();
    expect(document.activeElement).toBe(thumbnails()[1]);

    // A failed thumbnail shows the same text.
    root.querySelector('.photos__image')!.dispatchEvent(new Event('error'));
    await settle();
    expect(flat(root.querySelector('.photos__item'))).toBe('Photo unavailable.');

    // Assessment panel.
    const trigger = byText('button', 'View approved assessment', root)!;
    trigger.focus();
    await clickAndSettle(trigger);
    const dialog = document.body.querySelector('.p-dialog')!;
    expect(flat(dialog)).toContain('Approved assessment');
    expect(flat(dialog)).toContain('Completed Mon, Oct 5');
    expect(flat(dialog)).toContain('Corroded P-trap');
    expect(flat(dialog)).toContain('Replace trap and valve');
    expect(dialog.querySelectorAll('img')).toHaveLength(2);
    await clickAndSettle(
      document.body.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!,
    );
    expect(document.body.querySelector('.p-dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    // Report panel: Call office as tel:, coming soon text, Close, and no request.
    const report = byText('button', 'Report delay or issue', root)!;
    report.focus();
    await clickAndSettle(report);
    const panel = document.body.querySelector('.p-dialog')!;
    expect(flat(panel)).toContain('Report delay or issue');
    expect(flat(panel)).toContain('Issue reporting is coming soon.');
    expect(byText('a', 'Call office', panel)?.getAttribute('href')).toBe('tel:5125550199');
    await clickAndSettle(byText('button', 'Close', panel));
    expect(document.body.querySelector('.p-dialog')).toBeNull();
    expect(document.activeElement).toBe(report);

    // Without an office phone the call link is hidden.
    TestBed.resetTestingModule();
    const minimal = await open(MINIMAL);
    await clickAndSettle(byText('button', 'Report delay or issue', minimal));
    expect(byText('a', 'Call office', document.body)).toBeUndefined();
    expect(body()).toContain('Issue reporting is coming soon.');
    expect(byText('button', 'View approved assessment', minimal)).toBeUndefined();
  });

  it('shows the review placeholder with a way back for technicians only and writes nothing (AC-21)', async () => {
    await open();
    httpTesting.expectOne(URL).flush(visit('in_progress'));
    await harness.navigateByUrl('/today/visits/v-1/review');
    await settle();
    const root = harness.routeNativeElement as HTMLElement;
    expect(flat(root)).toContain('Review and completion is coming soon.');
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/today/visits/v-1');
    expect(flat(root)).toContain('Back to job');

    TestBed.resetTestingModule();
    await open(undefined, 'dispatcher');
    await harness.navigateByUrl('/today/visits/v-1/review');
    await settle();
    expect(flat(harness.routeNativeElement)).toContain("You don't have access to Today's jobs.");
  });

  it.each([
    [404, undefined, "This job isn't available.", true],
    [
      404,
      'technician_profile_not_linked',
      "Your team profile isn't linked yet. Contact your manager.",
      false,
    ],
    [
      403,
      'technician_inactive',
      'Your technician profile is inactive. Contact your manager.',
      false,
    ],
    [500, undefined, "We couldn't load this job. Try again.", false],
  ])('shows the %i state (%s) as "%s"', async (status, code, message, hasBack) => {
    const root = await open();
    httpTesting.expectOne(URL).flush({ status, code }, { status, statusText: 'Error' });
    await settle();

    expect(root.textContent).toContain(message);
    const back = Array.from(root.querySelectorAll('a')).find((a) =>
      a.textContent?.includes("Back to Today's jobs"),
    );
    expect(back?.getAttribute('href') ?? null).toBe(hasBack ? '/today' : null);
    expect(root.querySelector('h1')).toBeNull();
    expect(root.querySelector('.bar')).toBeNull();
  });

  it('shows Retry after a load failure and the forbidden state without a request for other roles', async () => {
    const root = await open();
    httpTesting.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    root.querySelector<HTMLButtonElement>('p-message button')!.click();
    await settle();
    expect(root.querySelector('p-skeleton')).not.toBeNull();
    httpTesting.expectOne(URL).flush(DETAIL);
    await settle();
    expect(root.querySelector('h1')?.textContent).toBe('Kitchen sink leak repair');

    TestBed.resetTestingModule();
    const other = await open(undefined, 'dispatcher');
    expect(other.textContent).toContain("You don't have access to Today's jobs.");
    httpTesting.expectNone(URL);
  });
});
