import { HttpEventType, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import {
  TechnicianVisitDetail,
  TodayVisitStatus,
  VisitTime,
} from '../../models/technician-visits.model';
import { deviationFor, formatEstimate } from '../../utils/job-progress';
import { VisitPhoto } from '../visit-photos/visit-photos';
import { JobProgress } from './job-progress';

const API = 'http://api.test';
const URL = `${API}/technician/visits/v-1`;
const NOW = new Date('2026-10-07T12:00:00Z').getTime();
const minutesAgo = (minutes: number) => new Date(NOW - minutes * 60_000).toISOString();

const BASE: TechnicianVisitDetail = {
  visitId: 'v-1',
  visitNumber: 2,
  workOrderId: 'wo-1',
  displayNumber: 'WO-3091',
  title: 'Kitchen sink leak repair',
  status: 'in_progress',
  start: '2026-10-07T09:30:00+13:00',
  end: '2026-10-07T11:30:00+13:00',
  arrivalWindowStart: null,
  arrivalWindowEnd: null,
  priority: 2,
  serviceCategory: 'Plumbing',
  customerName: 'Sofia Martinez',
  phone: null,
  address: {
    line1: '1842 Oak Street',
    line2: null,
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78704',
    countryCode: 'US',
  },
  latitude: null,
  longitude: null,
  plannedMaterialsCount: 1,
  isPrimary: true,
  date: '2026-10-07',
  timezone: 'Pacific/Auckland',
  dispatchNote: null,
  customerType: 'person',
  access: { instructions: null, contactPreference: null, activeDamage: false },
  instructions: null,
  scope: '- Replace P-trap\n- Test drainage',
  requiredSkills: [],
  officePhone: null,
  travel: { startedAt: null, arrivedAt: null, durationMinutes: null },
  plannedMaterials: [
    {
      id: 'm-1',
      description: 'P-trap',
      quantity: 2,
      unit: 'each',
      source: 'truck_stock',
      usedQuantity: 1,
    },
  ],
  tasks: [
    {
      id: 't-1',
      label: 'Replace P-trap',
      isRequired: true,
      isCompleted: false,
      notes: null,
      completedAt: null,
    },
    {
      id: 't-2',
      label: 'Clean up',
      isRequired: false,
      isCompleted: false,
      notes: 'Mop too',
      completedAt: null,
    },
  ],
  assessment: null,
  actualStartedAt: '2026-10-07T09:31:00+13:00',
  time: { workSeconds: 0, pauseSeconds: 0, activeEntry: null, estimatedMinutes: null },
  additionalMaterials: [
    { id: 'a-1', description: 'Sealant', quantity: 1, unit: 'tube', catalogItemId: null },
  ],
  evidence: [],
  technicianNotes: null,
};

function job(overrides: Partial<TechnicianVisitDetail> = {}): TechnicianVisitDetail {
  return { ...BASE, ...overrides };
}

function timed(time: Partial<VisitTime>, status: TodayVisitStatus = 'in_progress') {
  return job({ status, time: { ...BASE.time, ...time } });
}

describe('JobProgress', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<JobProgress>;
  let router: Router;
  let changes: TechnicianVisitDetail[];
  let reloads: number;

  const photosOf = (visit: TechnicianVisitDetail): VisitPhoto[] =>
    visit.evidence.map((item) => ({
      id: item.id,
      url: `${URL}/evidence/${item.id}`,
      label: item.type === 'before' ? 'Before' : 'After',
    }));

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  async function mount(visit: TechnicianVisitDetail): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(JobProgress);
    changes = [];
    reloads = 0;
    // The page replaces the visit with every response; the harness does the same.
    fixture.componentInstance.visitChange.subscribe((next) => {
      changes.push(next);
      fixture.componentRef.setInput('visit', next);
      fixture.componentRef.setInput('photos', photosOf(next));
    });
    fixture.componentInstance.reload.subscribe(() => reloads++);
    fixture.componentRef.setInput('visit', visit);
    fixture.componentRef.setInput('photos', photosOf(visit));
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  const flat = (node: Element | null | undefined) =>
    (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const buttons = (scope: ParentNode = document) =>
    Array.from(scope.querySelectorAll<HTMLButtonElement>('button'));
  const button = (label: string, scope: ParentNode = document) =>
    buttons(scope).find((b) => flat(b) === label || b.getAttribute('aria-label') === label);
  const field = <T extends HTMLElement>(label: string) =>
    document.querySelector<T>(`[aria-label="${label}"]`)!;
  const press = async (element: HTMLElement | undefined) => {
    element!.click();
    await settle();
  };
  const type = async (element: HTMLInputElement | HTMLTextAreaElement, value: string) => {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle();
  };
  const reply = async (
    match: { method: string; url: string },
    body: TechnicianVisitDetail | null,
    failure?: { status: number; code?: string },
  ) => {
    const request = httpTesting.expectOne(match);
    if (failure) {
      request.flush(
        { status: failure.status, code: failure.code },
        { status: failure.status, statusText: 'Error' },
      );
    } else {
      request.flush(body);
    }
    await settle();
    return request.request;
  };

  // The test DOM has no ResizeObserver, which PrimeNG overlays may use.
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
    vi.useRealTimers();
    vi.unstubAllGlobals();
    httpTesting.verify();
  });

  it.each<[string, TechnicianVisitDetail, string[], string[]]>([
    [
      'in progress with estimate',
      timed({
        workSeconds: 3600,
        pauseSeconds: 300,
        activeEntry: { type: 'work', startedAt: minutesAgo(41) },
        estimatedMinutes: 120,
      }),
      ['In progress • 1h 41m', 'Started 9:31 AM', 'Pause'],
      ['1h 41m Live', '5m', '2h', '19 min under estimate'],
    ],
    [
      'paused with an active pause',
      timed(
        {
          workSeconds: 3600,
          pauseSeconds: 60,
          activeEntry: { type: 'pause', startedAt: minutesAgo(5) },
          estimatedMinutes: 90,
        },
        'paused',
      ),
      ['Paused • 1h 0m', 'Resume'],
      ['1h 0m', '6m', '1h 30m', '30 min under estimate'],
    ],
    [
      'no estimate, clock ahead of the server',
      timed({
        workSeconds: 600,
        activeEntry: { type: 'work', startedAt: new Date(NOW + 60_000).toISOString() },
      }),
      ['In progress • 10m'],
      ['10m Live', '0m'],
    ],
    [
      'over estimate',
      timed({ workSeconds: 4200, estimatedMinutes: 60 }),
      ['In progress • 1h 10m'],
      ['10 min over estimate'],
    ],
    [
      'on estimate',
      timed({ workSeconds: 3600, estimatedMinutes: 60 }),
      ['1h 0m'],
      ['1h 0m Live', '1h', 'On estimate'],
    ],
  ])(
    'shows the banner, Pause or Resume and the time summary for %s (AC-17)',
    async (name, visit, banner, summary) => {
      vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
      vi.setSystemTime(NOW);
      const root = await mount(visit);

      const bannerText = flat(root.querySelector('.banner'));
      for (const part of banner) {
        expect(bannerText).toContain(part);
      }
      expect(button('Report issue', root)).toBeDefined();
      const paused = visit.status === 'paused';
      expect(button(paused ? 'Pause' : 'Resume', root)).toBeUndefined();
      const time = flat(root.querySelector('app-job-time'));
      for (const part of summary) {
        expect(time).toContain(part);
      }
      expect(time.includes('Live')).toBe(!paused);
      expect(time.includes('Estimated')).toBe(visit.time.estimatedMinutes !== null);

      if (name === 'in progress with estimate') {
        // The labor time refreshes without a response (BR-18).
        vi.advanceTimersByTime(60_000);
        await settle();
        expect(flat(root.querySelector('.banner'))).toContain('In progress • 1h 42m');
        expect(flat(root.querySelector('app-job-time'))).toContain('18 min under estimate');

        // Pure formats and boundaries.
        expect([0, 59, 60, 90, 120, 135].map(formatEstimate)).toEqual([
          '0m',
          '59m',
          '1h',
          '1h 30m',
          '2h',
          '2h 15m',
        ]);
        expect(deviationFor(3599, 60)?.text).toBe('1 min under estimate');
        expect(deviationFor(3659, 60)?.text).toBe('On estimate');
        expect(deviationFor(10, null)).toBeNull();
      }
    },
  );

  it('keeps local input and shows the fixed message for 400, 409 and other failures (AC-18, AC-19, AC-20)', async () => {
    const root = await mount(job());
    const alert = () =>
      Array.from(root.querySelectorAll('[role="alert"]'), (node) => flat(node)).join(' | ');

    // Pause: pending blocks a repeat; 409 shows its message and reloads.
    button('Pause', root)!.click();
    button('Pause', root)!.click();
    await settle();
    await reply({ method: 'POST', url: `${URL}/pause` }, null, {
      status: 409,
      code: 'visit_status_invalid',
    });
    expect(alert()).toContain("This job can't be paused or resumed in its current state.");
    expect(reloads).toBe(1);

    // Task completion: 409 reloads and the checkbox goes back.
    const box = root.querySelector<HTMLInputElement>('app-job-tasks input[type="checkbox"]')!;
    box.click();
    await settle();
    const patch = await reply({ method: 'PATCH', url: `${URL}/tasks/t-1` }, null, {
      status: 409,
      code: 'visit_status_invalid',
    });
    expect(patch.body).toEqual({ isCompleted: true });
    expect(alert()).toContain("This job can't be updated in its current state.");
    expect(box.checked).toBe(false);
    expect(reloads).toBe(2);

    // Comment: 400 keeps the editor and the typed text.
    await press(button('Comment on Clean up', root));
    expect(field<HTMLTextAreaElement>('Comment for Clean up').value).toBe('Mop too');
    await type(field<HTMLTextAreaElement>('Comment for Clean up'), 'Mop and dry');
    await press(button('Save', root));
    await reply({ method: 'PATCH', url: `${URL}/tasks/t-2` }, null, { status: 400 });
    expect(alert()).toContain('Enter a comment up to 1000 characters.');
    expect(field<HTMLTextAreaElement>('Comment for Clean up').value).toBe('Mop and dry');
    await press(button('Cancel', root));
    expect(root.querySelector('textarea[aria-label="Comment for Clean up"]')).toBeNull();

    // Used quantity: invalid decimals send nothing; another failure keeps the typed value.
    const used = field<HTMLInputElement>('Used quantity for P-trap');
    await type(used, '1.2345');
    used.dispatchEvent(new Event('change'));
    await settle();
    expect(alert()).toContain('Enter a quantity from 0 to 99999.999 with up to 3 decimals.');
    await type(used, '1.5');
    used.dispatchEvent(new Event('change'));
    await settle();
    const put = await reply({ method: 'PUT', url: `${URL}/planned-materials/m-1` }, null, {
      status: 500,
    });
    expect(put.body).toEqual({ usedQuantity: 1.5 });
    expect(alert()).toContain("We couldn't update this job. Try again.");
    expect(used.value).toBe('1.5');

    // Stepper: one request for repeated taps; an additional material reaching 0 asks first.
    const plus = button('Increase P-trap', root)!;
    plus.click();
    plus.click();
    await settle();
    await reply(
      { method: 'PUT', url: `${URL}/planned-materials/m-1` },
      job({ plannedMaterials: [{ ...BASE.plannedMaterials[0], usedQuantity: 2 }] }),
    );
    expect(field<HTMLInputElement>('Used quantity for P-trap').value).toBe('2');
    expect(flat(root.querySelector('app-job-materials'))).toContain(
      'Differs from planned quantities',
    );
    await press(button('Decrease Sealant', root));
    expect(flat(document.querySelector('.p-dialog'))).toContain('Remove Sealant?');
    await press(button('Cancel', document.body));
    await press(button('Decrease Sealant', root));
    await press(button('Remove', document.body));
    const removal = await reply(
      { method: 'PUT', url: `${URL}/materials/a-1` },
      job({
        additionalMaterials: [],
        plannedMaterials: [{ ...BASE.plannedMaterials[0], usedQuantity: 2 }],
      }),
    );
    expect(removal.body).toEqual({ quantity: 0 });
    expect(flat(root.querySelector('app-job-materials'))).toContain('Matches planned quantities');
  });

  it('uploads Before and After photos with progress, revokes previews and maps 413 and 400 (AC-20)', async () => {
    const create = vi.fn(() => 'blob:preview');
    const revoke = vi.fn();
    vi.stubGlobal(
      'URL',
      Object.assign(globalThis.URL, { createObjectURL: create, revokeObjectURL: revoke }),
    );
    const root = await mount(job());
    const input = root.querySelector<HTMLInputElement>('input[type="file"]')!;
    const pick = async (file: File) => {
      Object.defineProperty(input, 'files', {
        configurable: true,
        value: { length: 1, item: () => file },
      });
      input.dispatchEvent(new Event('change'));
      await settle();
    };
    const png = new File(['x'], 'a.png', { type: 'image/png' });
    expect(flat(root.querySelector('app-job-evidence'))).toContain('No photos yet.');
    expect(flat(root.querySelector('app-job-evidence'))).toContain(
      'Add at least one before and one after photo',
    );

    // Add photo asks Before or After, then opens the picker.
    const click = vi.spyOn(input, 'click');
    await press(button('Add photo', root));
    expect(Array.from(root.querySelectorAll('[role="group"] button'), (b) => flat(b))).toEqual([
      'Before',
      'After',
      'Cancel',
    ]);
    await press(button('Before', root));
    expect(click).toHaveBeenCalledOnce();
    expect(input.accept).toBe('image/jpeg,image/png');

    // Client pre-check: wrong type or size never reaches the API.
    await pick(new File(['x'], 'a.gif', { type: 'image/gif' }));
    expect(flat(root.querySelector('[role="alert"]'))).toBe(
      'Upload a JPEG or PNG image up to 10 MB.',
    );
    expect(create).not.toHaveBeenCalled();

    // 413 and 400 map to the photo message; the preview is revoked each time.
    for (const status of [413, 400]) {
      await pick(png);
      const request = httpTesting.expectOne({ method: 'POST', url: `${URL}/evidence` });
      expect((request.request.body as FormData).get('type')).toBe('before');
      request.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
      await settle();
      expect(root.querySelector('img[src="blob:preview"]')).not.toBeNull();
      expect(
        root
          .querySelector('[role="progressbar"][aria-label="Upload progress"]')
          ?.getAttribute('aria-valuenow'),
      ).toBe('50');
      expect(button('Add photo', root)!.disabled).toBe(true);
      request.flush({ status }, { status, statusText: 'Error' });
      await settle();
      expect(flat(root.querySelector('[role="alert"]'))).toBe(
        'Upload a JPEG or PNG image up to 10 MB.',
      );
      expect(revoke).toHaveBeenLastCalledWith('blob:preview');
      expect(root.querySelector('img[src="blob:preview"]')).toBeNull();
      await press(button('Add photo', root));
      await press(button('Before', root));
    }

    // Success: the response replaces the visit; thumbnails are labeled and counted per type.
    await pick(png);
    const done = job({
      evidence: [
        { id: 'e-1', type: 'before', createdAt: '2026-10-07T10:00:00+13:00' },
        { id: 'e-2', type: 'after', createdAt: '2026-10-07T10:05:00+13:00' },
      ],
    });
    httpTesting.expectOne({ method: 'POST', url: `${URL}/evidence` }).flush(done);
    await settle();
    expect(revoke).toHaveBeenCalledTimes(3);
    const section = root.querySelector('app-job-evidence')!;
    expect(flat(section)).toContain('Before and after photos added');
    expect(flat(section)).toContain('Before');
    expect(
      Array.from(section.querySelectorAll('img'), (img) => [img.alt, img.getAttribute('src')]),
    ).toEqual([
      ['Before photo 1 of 1', `${URL}/evidence/e-1`],
      ['After photo 1 of 1', `${URL}/evidence/e-2`],
    ]);
    expect(flat(root.querySelector('.progress__sr'))).toBe('Photo added.');
  });

  it('keeps every section on the page and scrolls to the one picked in the navigation (BR-16)', async () => {
    let observed: IntersectionObserverCallback = () => undefined;
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(callback: IntersectionObserverCallback) {
          observed = callback;
        }
        observe = vi.fn();
        unobserve = vi.fn();
        disconnect = vi.fn();
      },
    );
    const root = await mount(job());
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    const link = (label: string) =>
      Array.from(root.querySelectorAll<HTMLAnchorElement>('nav a')).find((a) => flat(a) === label)!;
    for (const section of [
      'app-job-tasks',
      'app-job-materials',
      'app-job-evidence',
      'app-job-notes',
    ]) {
      expect(root.querySelector(section)).not.toBeNull();
    }
    expect(link('Tasks').getAttribute('aria-current')).toBe('location');

    await press(link('Materials'));
    expect(scroll).toHaveBeenCalledOnce();
    expect((scroll.mock.contexts[0] as HTMLElement).id).toBe('job-section-materials');
    expect(link('Materials').getAttribute('aria-current')).toBe('location');
    expect(link('Tasks').hasAttribute('aria-current')).toBe(false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // The mark follows the section scrolled into view (AC-20).
    observed(
      [
        {
          isIntersecting: true,
          target: root.querySelector('[data-section="photos"]')!,
        } as unknown as IntersectionObserverEntry,
      ],
      {} as IntersectionObserver,
    );
    await settle();
    expect(link('Photos').getAttribute('aria-current')).toBe('location');
    expect(link('Materials').hasAttribute('aria-current')).toBe(false);

    // Original scope: numbered read-only lines; Close dismisses the dialog.
    await press(button('View original scope', root));
    const dialog = () => document.body.querySelector('.scope');
    expect(flat(dialog())).toContain('2 scope items Read only');
    expect(
      Array.from(document.body.querySelectorAll('.scope__item'), (item) =>
        [item.querySelector('.scope__number'), item.querySelector('.scope__text')].map(flat),
      ),
    ).toEqual([
      ['01', 'Replace P-trap'],
      ['02', 'Test drainage'],
    ]);
    await press(button('Close', dialog()!));
    expect(dialog()).toBeNull();
  });

  it.each<[string, TechnicianVisitDetail, string[], string[]]>([
    [
      'incomplete required task with a comment',
      job(),
      ['0 of 2 tasks completed', '0%', '1 required task remaining', 'Required'],
      ['Has a comment'],
    ],
    [
      'two required tasks incomplete',
      job({
        tasks: [
          ...BASE.tasks,
          { ...BASE.tasks[0], id: 't-3', label: 'Test drainage', notes: null },
        ],
      }),
      ['0 of 3 tasks completed', '2 required tasks remaining'],
      [],
    ],
    [
      'completed with completion times',
      job({
        tasks: BASE.tasks.map((task, index) => ({
          ...task,
          isCompleted: index === 0,
          completedAt: index === 0 ? '2026-10-07T10:15:00+13:00' : null,
        })),
      }),
      ['1 of 2 tasks completed', '50%', 'All required tasks complete', '10:15 AM'],
      [],
    ],
    [
      'no tasks and no materials',
      job({ tasks: [], plannedMaterials: [], additionalMaterials: [] }),
      ['No tasks for this job.', 'No materials recorded.', 'Add task', 'Add material'],
      [],
    ],
  ])(
    'shows the Tasks and Materials section states for %s (AC-18, AC-19)',
    async (_name, visit, texts, extra) => {
      const root = await mount(visit);
      const text = flat(root.querySelector('app-job-tasks')) + ' ' + flat(root);
      for (const part of [...texts, ...extra]) {
        expect(text).toContain(part);
      }
      expect(root.querySelector('app-job-tasks [role="progressbar"]') === null).toBe(
        visit.tasks.length === 0,
      );
      expect(button('View original scope', root)).toBeDefined();
    },
  );

  it('adds a task and a catalog or custom material, never steps below 0 (AC-18, AC-19)', async () => {
    const root = await mount(
      job({ plannedMaterials: [{ ...BASE.plannedMaterials[0], usedQuantity: 0 }] }),
    );
    expect(button('Decrease P-trap', root)!.disabled).toBe(true);

    // Add task: label field with Add / Cancel; the response closes the form.
    await press(button('Add task', root));
    expect(button('Add', root)!.disabled).toBe(true);
    await type(field<HTMLInputElement>('Task name'), 'Check valve');
    await press(button('Add', root));
    const added = await reply(
      { method: 'POST', url: `${URL}/tasks` },
      job({
        tasks: [
          ...BASE.tasks,
          { ...BASE.tasks[0], id: 't-3', label: 'Check valve', isRequired: false },
        ],
        plannedMaterials: [{ ...BASE.plannedMaterials[0], usedQuantity: 0 }],
      }),
    );
    expect(added.body).toEqual({ label: 'Check valve' });
    expect(root.querySelector('[aria-label="Task name"]')).toBeNull();
    expect(flat(root.querySelector('app-job-tasks'))).toContain('Check valve');

    // Add material from the catalog: 2+ characters search, results show name and unit.
    const dialog = () => document.querySelector('.p-dialog')!;
    const search = () => dialog().querySelector<HTMLInputElement>('input[type="search"]')!;
    const wait = () => new Promise((resolve) => setTimeout(resolve, 350));
    await press(button('Add material', root));
    expect(button('Add material', dialog())!.disabled).toBe(true);
    await type(search(), 'p');
    await wait();
    httpTesting.expectNone({ method: 'GET', url: `${URL}/material-catalog?search=p` });
    await type(search(), 'trap');
    await wait();
    httpTesting
      .expectOne({ method: 'GET', url: `${URL}/material-catalog?search=trap` })
      .flush([{ id: 'c-1', name: 'P-trap 1.5 in', unit: 'each' }]);
    await settle();
    expect(flat(dialog().querySelector('.add__result'))).toBe('P-trap 1.5 in each');
    await press(dialog().querySelector<HTMLButtonElement>('.add__result')!);
    await press(button('Add material', dialog()));
    const catalog = await reply({ method: 'POST', url: `${URL}/materials` }, job());
    expect(catalog.body).toEqual({ quantity: 1, catalogItemId: 'c-1' });
    expect(document.querySelector('.p-dialog')).toBeNull();

    // Custom material needs description, unit and a quantity.
    await press(button('Add material', root));
    await press(button('Custom material', dialog()));
    expect(button('Add material', dialog())!.disabled).toBe(true);
    const [description, unit] = Array.from(
      dialog().querySelectorAll<HTMLInputElement>('input.add__input'),
    );
    await type(description, 'Flux');
    await type(unit, 'tube');
    await press(button('Add material', dialog()));
    const custom = await reply({ method: 'POST', url: `${URL}/materials` }, null, {
      status: 409,
      code: 'material_limit_reached',
    });
    expect(custom.body).toEqual({ quantity: 1, description: 'Flux', unit: 'tube' });
    expect(flat(dialog())).toContain('This job already has the maximum number of materials.');
    expect(reloads).toBe(1);
  });

  it('gates Review & complete, saves notes before Save and exit and keeps non-primary read-only (AC-21)', async () => {
    const root = await mount(job());
    const review = () => button('Review & complete', root)!;
    const notes = () => root.querySelector<HTMLTextAreaElement>('app-job-notes textarea')!;
    expect(review().disabled).toBe(true);
    expect(flat(root.querySelector('.bar'))).toContain(
      'Complete required tasks and add before and after photos',
    );

    // Notes: counter, blur saves with "Saved"; Save and exit saves then opens Today.
    expect(flat(root.querySelector('app-job-notes'))).toContain('0/4000');
    expect(flat(root.querySelector('app-job-notes'))).toContain(
      'Visible to office; include in completion report',
    );
    await type(notes(), '  Replaced trap ');
    expect(flat(root.querySelector('app-job-notes'))).toContain('16/4000');
    notes().dispatchEvent(new Event('blur'));
    await settle();
    const saved = await reply(
      { method: 'PUT', url: `${URL}/notes` },
      job({ technicianNotes: 'Replaced trap' }),
    );
    expect(saved.body).toEqual({ notes: 'Replaced trap' });
    expect(flat(root.querySelector('app-job-notes'))).toContain('Saved');

    await type(notes(), 'Replaced trap and valve');
    await press(button('Save and exit', root));
    await reply({ method: 'PUT', url: `${URL}/notes` }, null, { status: 500 });
    expect(flat(root.querySelector('.bar'))).toContain("We couldn't save your notes. Try again.");
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    await press(button('Save and exit', root));
    await reply(
      { method: 'PUT', url: `${URL}/notes` },
      job({ technicianNotes: 'Replaced trap and valve' }),
    );
    expect(router.navigateByUrl).toHaveBeenLastCalledWith('/today');
    await press(button('Save and exit', root));
    expect(router.navigateByUrl).toHaveBeenCalledTimes(2);

    // Ready: required tasks complete plus Before and After photos.
    const ready = job({
      technicianNotes: 'Replaced trap and valve',
      tasks: BASE.tasks.map((task) => ({ ...task, isCompleted: task.isRequired })),
      evidence: [
        { id: 'e-1', type: 'before', createdAt: '2026-10-07T10:00:00+13:00' },
        { id: 'e-2', type: 'after', createdAt: '2026-10-07T10:05:00+13:00' },
      ],
    });
    fixture.componentRef.setInput('visit', ready);
    fixture.componentRef.setInput('photos', photosOf(ready));
    await settle();
    expect(review().disabled).toBe(false);
    expect(flat(root.querySelector('.bar'))).toContain('Next: customer review and signature');
    await press(review());
    expect(router.navigate).toHaveBeenCalledWith(['/today/visits', 'v-1', 'review']);

    // Non-primary: every mutation control is gone and inputs are read-only.
    fixture.componentRef.setInput('visit', { ...ready, isPrimary: false });
    await settle();
    const text = flat(root);
    expect(text).toContain('The primary technician manages this job.');
    expect(text).toContain("Back to Today's jobs");
    for (const label of [
      'Pause',
      'Review & complete',
      'Save and exit',
      'Add task',
      'Add material',
      'Add photo',
    ]) {
      expect(button(label, root)).toBeUndefined();
    }
    expect(
      root.querySelectorAll<HTMLInputElement>('app-job-tasks input[type="checkbox"]')[0].disabled,
    ).toBe(true);
    expect(field<HTMLInputElement>('Used quantity for P-trap').readOnly).toBe(true);
    expect(button('Increase P-trap', root)!.disabled).toBe(true);
    expect(notes().readOnly).toBe(true);
    expect(button('Report issue', root)).toBeDefined();
  });
});
