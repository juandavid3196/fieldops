import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { VisitReview } from './visit-review';

const API = 'http://api.test';
const URL = `${API}/technician/visits/v-1`;
const COMPLETE_URL = `${URL}/complete`;

@Component({ selector: 'app-stub', template: '' })
class Stub {}

const READY = {
  requiredTasksComplete: true,
  hasBeforePhoto: true,
  hasAfterPhoto: true,
  ready: true,
  completesWorkOrder: true,
};

const BASE: TechnicianVisitDetail = {
  visitId: 'v-1',
  visitNumber: 1,
  workOrderId: 'wo-1',
  displayNumber: 'WO-1048',
  title: 'Kitchen sink leak repair',
  status: 'in_progress',
  start: '2026-10-07T09:30:00+13:00',
  end: '2026-10-07T11:30:00+13:00',
  arrivalWindowStart: null,
  arrivalWindowEnd: null,
  priority: 3,
  serviceCategory: 'Plumbing',
  customerName: 'Sofia Martinez',
  phone: null,
  address: {
    line1: '742 Maple Ave',
    line2: null,
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78704',
    countryCode: 'US',
  },
  latitude: null,
  longitude: null,
  plannedMaterialsCount: 2,
  isPrimary: true,
  date: '2026-10-07',
  timezone: 'Pacific/Auckland',
  dispatchNote: null,
  customerType: 'person',
  access: { instructions: null, contactPreference: null, activeDamage: false },
  instructions: null,
  scope: '',
  requiredSkills: [],
  officePhone: null,
  travel: { startedAt: null, arrivedAt: null, durationMinutes: null },
  plannedMaterials: [
    {
      id: 'm-1',
      description: 'Brass valve',
      quantity: 2,
      unit: 'each',
      source: 'truck_stock',
      usedQuantity: 1.5,
    },
    {
      id: 'm-2',
      description: 'Unused tape',
      quantity: 1,
      unit: 'roll',
      source: 'truck_stock',
      usedQuantity: 0,
    },
  ],
  tasks: [
    {
      id: 't-1',
      label: 'Fix',
      isRequired: true,
      isCompleted: true,
      notes: null,
      completedAt: null,
    },
    {
      id: 't-2',
      label: 'Clean',
      isRequired: false,
      isCompleted: false,
      notes: null,
      completedAt: null,
    },
  ],
  assessment: null,
  actualStartedAt: '2026-10-07T09:31:00+13:00',
  time: { workSeconds: 7560, pauseSeconds: 0, activeEntry: null, estimatedMinutes: null },
  additionalMaterials: [
    { id: 'a-1', description: 'Fittings', quantity: 1, unit: 'set', catalogItemId: null },
    { id: 'a-2', description: 'Sealant', quantity: 2, unit: 'tube', catalogItemId: null },
    { id: 'a-3', description: 'Gasket', quantity: 3, unit: 'each', catalogItemId: null },
  ],
  evidence: [
    { id: 'e-1', type: 'before', createdAt: '2026-10-07T09:40:00+13:00' },
    { id: 'e-2', type: 'after', createdAt: '2026-10-07T10:40:00+13:00' },
    { id: 'e-3', type: 'after', createdAt: '2026-10-07T10:41:00+13:00' },
  ],
  technicianNotes: 'Replaced the valve.',
  primaryTechnicianName: 'Carlos Rivera',
  completion: READY,
};

const NOT_READY: Partial<TechnicianVisitDetail> = {
  plannedMaterials: [],
  additionalMaterials: [],
  evidence: [],
  technicianNotes: null,
  completion: {
    requiredTasksComplete: false,
    hasBeforePhoto: false,
    hasAfterPhoto: false,
    ready: false,
    completesWorkOrder: false,
  },
};

const job = (overrides: Partial<TechnicianVisitDetail> = {}): TechnicianVisitDetail => ({
  ...BASE,
  ...overrides,
});

describe('VisitReview', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    harness.fixture.detectChanges();
  }

  async function open(body: TechnicianVisitDetail, roleCode = 'technician'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'today/visits/:visitId/review', component: VisitReview },
          { path: 'today/visits/:visitId', component: Stub },
          { path: 'today', component: Stub },
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
    await harness.navigateByUrl('/today/visits/v-1/review');
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(body);
    await settle();
    root = harness.routeNativeElement as HTMLElement;
  }

  const flat = (node: Element | null | undefined) =>
    (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      flat(b).includes(label),
    );
  const click = async (element: HTMLElement | null | undefined) => {
    element?.click();
    await settle();
  };
  const field = <T extends HTMLElement>(id: string) => root.querySelector<T>(`#${id}`);

  async function chooseMethod(label: string): Promise<void> {
    const radio = Array.from(root.querySelectorAll<HTMLInputElement>('input[type=radio]')).find(
      (input) => flat(input.closest('label')).includes(label),
    );
    radio?.click();
    await settle();
  }

  async function type(id: string, value: string): Promise<void> {
    const input = field<HTMLInputElement>(id)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function sign(): Promise<void> {
    const canvas = root.querySelector('canvas')!;
    const pointer = (type: string, x: number) =>
      canvas.dispatchEvent(
        Object.assign(new MouseEvent(type, { clientX: x, clientY: 5, bubbles: true }), {
          pointerId: 1,
        }),
      );
    pointer('pointerdown', 1);
    pointer('pointermove', 4);
    pointer('pointerup', 6);
    await settle();
  }

  async function toAcknowledgment(body: TechnicianVisitDetail = job()): Promise<void> {
    await open(body);
    await click(button('Continue to acknowledgment'));
  }

  beforeEach(() => {
    const context = {
      fillRect: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
      arc: vi.fn(),
      fill: vi.fn(),
    };
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      context as unknown as CanvasRenderingContext2D,
    );
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) =>
      callback(new Blob(['png'], { type: 'image/png' })),
    );
  });

  afterEach(() => {
    httpTesting.verify();
    vi.restoreAllMocks();
  });

  it('renders the review step and gates Continue on completion.ready (BR-12, BR-13, BR-18)', async () => {
    await open(job());
    expect(root.querySelector('h1')?.textContent).toContain('Review & complete');
    const text = flat(root);
    for (const expected of [
      'Review completed work before capturing customer acknowledgment.',
      'Kitchen sink leak repair',
      'Sofia Martinez',
      '742 Maple Ave, Austin, TX 78704',
      '1 of 2 tasks complete.',
      '1/2',
      'Brass valve × 1.5',
      'Fittings × 1',
      'Sealant × 2',
      '+1 more',
      'Before photos: 1',
      'After photos: 2',
      'Replaced the valve.',
      '2h 6m',
      'Carlos Rivera',
      'Ready to complete',
      'Checklist complete',
      'Required evidence attached',
      'Materials recorded',
    ]) {
      expect(text).toContain(expected);
    }
    expect(text).not.toContain('Unused tape');
    expect(button('Continue to acknowledgment')?.disabled).toBe(false);
    const links = Array.from(root.querySelectorAll('a.review__row')).map((a) =>
      a.getAttribute('href'),
    );
    expect(links).toEqual([
      '/today/visits/v-1#job-section-tasks',
      '/today/visits/v-1#job-section-materials',
      '/today/visits/v-1#job-section-photos',
      '/today/visits/v-1#job-section-notes',
    ]);
    // Top bar data for the shell: number, chip and labor time.
    expect(TestBed.inject(TechnicianVisitsService).shellHeader()).toMatchObject({
      title: '#WO-1048',
      status: 'In progress',
      backLabel: 'Back to job',
      time: '2h 6m',
    });

    TestBed.resetTestingModule();
    await open(job(NOT_READY));
    const blocked = flat(root);
    for (const expected of [
      'Not ready to complete',
      'Complete required tasks and add before and after photos',
      'Required tasks incomplete',
      'Before and after photos missing',
      'No materials recorded',
      'No materials recorded.',
      'No completion note.',
    ]) {
      expect(blocked).toContain(expected);
    }
    expect(button('Continue to acknowledgment')?.disabled).toBe(true);
  });

  it.each([
    [
      'Signature obtained',
      [
        'Signer name',
        'Relationship',
        'Customer signature',
        'Customer comment (optional)',
        'Customer confirms the work was reviewed.',
      ],
      [
        "Enter the signer's name.",
        "Add the customer's signature.",
        'Confirm that the customer reviewed the work.',
      ],
      'ack-signerName',
    ],
    ['Customer not available', ['Reason'], ['Enter the reason.'], 'ack-comment'],
    [
      'Customer declined to sign',
      ['Signer name (optional)', 'Reason'],
      ['Enter the reason.'],
      'ack-comment',
    ],
    [
      'Remote confirmation',
      [
        'Signer name',
        'Relationship',
        'Confirmation details',
        'Customer confirms the work was reviewed.',
      ],
      [
        "Enter the signer's name.",
        'Enter how the customer confirmed.',
        'Confirm that the customer reviewed the work.',
      ],
      'ack-signerName',
    ],
  ])(
    'shows the fields of "%s" and validates them without a request (BR-14, BR-16)',
    async (label, shown, messages, focusId) => {
      await toAcknowledgment();
      expect(root.querySelector('h1')?.textContent).toContain('Customer acknowledgment');
      expect(document.activeElement).toBe(root.querySelector('h1'));
      expect(flat(root)).toContain(
        'A signature confirms service acknowledgment. It does not confirm payment.',
      );
      expect(flat(root)).toContain(
        'Completing stops the timer and changes the work order to Completed.',
      );
      expect(field<HTMLInputElement>('ack-signerName')?.value).toBe('Sofia Martinez');

      await chooseMethod(label);
      const form = flat(root.querySelector('form'));
      for (const expected of shown) {
        expect(form).toContain(expected);
      }
      if (label !== 'Signature obtained') {
        expect(form).not.toContain('Customer signature');
      }
      if (label === 'Customer not available') {
        expect(field('ack-signerName')).toBeNull();
      }

      if (field('ack-signerName') !== null) {
        await type('ack-signerName', '');
      }
      await click(button('Complete job'));
      for (const message of messages) {
        expect(flat(root)).toContain(message);
      }
      expect(root.querySelectorAll('[aria-invalid="true"]').length).toBeGreaterThan(0);
      expect(document.activeElement?.id).toBe(focusId);
      httpTesting.expectNone(COMPLETE_URL);
    },
  );

  it('submits only the allowed fields, protects against repeats and handles failures (BR-16, BR-17)', async () => {
    await toAcknowledgment();
    expect(button('Back')).toBeDefined();
    await sign();
    expect(flat(root)).toContain('Signature captured');
    await click(field('ack-reviewConfirmed'));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    await click(button('Complete job'));
    await settle();
    const request = httpTesting.expectOne({ method: 'POST', url: COMPLETE_URL });
    const body = request.request.body as FormData;
    expect(Array.from(body.keys()).sort()).toEqual([
      'method',
      'relationship',
      'reviewConfirmed',
      'signature',
      'signerName',
    ]);
    expect(body.get('method')).toBe('signed');
    expect(body.get('reviewConfirmed')).toBe('true');
    expect((body.get('signature') as File).name).toBe('signature.png');
    expect(button('Complete job')?.disabled).toBe(true);
    expect(button('Back')?.disabled).toBe(true);

    // Other failure: message shown, every value and the signature kept.
    request.flush({}, { status: 500, statusText: 'Error' });
    await settle();
    expect(flat(root)).toContain("We couldn't complete this job. Try again.");
    expect(flat(root)).toContain('Signature captured');
    expect(field<HTMLInputElement>('ack-signerName')?.value).toBe('Sofia Martinez');
    expect(field<HTMLInputElement>('ack-reviewConfirmed')?.checked).toBe(true);

    // 400: fixed field messages.
    await click(button('Complete job'));
    await settle();
    httpTesting
      .expectOne(COMPLETE_URL)
      .flush({ errors: { signature: ['x'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();
    expect(flat(root)).toContain('Capture the signature again.');

    // 409: message, reload and back to the Review step.
    await click(button('Complete job'));
    await settle();
    httpTesting
      .expectOne(COMPLETE_URL)
      .flush({ code: 'completion_requirements_unmet' }, { status: 409, statusText: 'Conflict' });
    await settle();
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(job());
    await settle();
    expect(root.querySelector('h1')?.textContent).toContain('Review & complete');
    expect(flat(root)).toContain(
      'Complete required tasks and add before and after photos before completing this job.',
    );
    expect(navigate).not.toHaveBeenCalled();

    // Success opens Today with the toast handoff.
    await click(button('Continue to acknowledgment'));
    await click(button('Complete job'));
    await settle();
    httpTesting
      .expectOne(COMPLETE_URL)
      .flush({ changed: false, visit: job({ status: 'completed' }) });
    await settle();
    expect(navigate).toHaveBeenCalledWith(['/today'], {
      state: { toast: { severity: 'success', summary: 'Job completed.' } },
    });
  });

  it('keeps non-primary technicians read-only and redirects other statuses (BR-17)', async () => {
    await open(job({ isPrimary: false }));
    expect(flat(root)).toContain('The primary technician manages this job.');
    expect(button('Continue to acknowledgment')).toBeUndefined();
    expect(root.querySelector('a.bar__button')?.getAttribute('href')).toBe('/today/visits/v-1');
    expect(root.querySelector('form')).toBeNull();

    TestBed.resetTestingModule();
    await toAcknowledgment();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await sign();
    await click(field('ack-reviewConfirmed'));
    await click(button('Complete job'));
    httpTesting
      .expectOne(COMPLETE_URL)
      .flush({ code: 'not_primary_technician' }, { status: 403, statusText: 'Forbidden' });
    await settle();
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(job({ isPrimary: false }));
    await settle();
    expect(root.querySelector('form')).toBeNull();
    expect(flat(root)).toContain('The primary technician manages this job.');
    expect(navigate).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    await open(job({ status: 'assigned' }));
    expect(TestBed.inject(Router).url).toBe('/today/visits/v-1');
  });
});
