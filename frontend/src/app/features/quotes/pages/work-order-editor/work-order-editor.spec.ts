import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  WO_CHANGED_MESSAGE,
  WO_FORBIDDEN_MESSAGE,
  WO_NOT_APPROVED_MESSAGE,
  WorkOrderEditor as EditorModel,
} from '../../../jobs/models/work-order.model';
import { workOrderEditorUnsavedChangesGuard } from '../../guards/work-order-editor-unsaved-changes.guard';
import { WorkOrderEditor } from './work-order-editor';

const API = 'http://api.test';
const QUOTE_ID = '33333333-3333-4333-8333-333333333333';
const REQUEST_ID = '11111111-1111-4111-8111-111111111111';
const UPDATED_AT = '2026-10-06T12:00:00Z';

@Component({ template: '<p>stub</p>' })
class Stub {}

const editorModel = (patch: Partial<EditorModel> = {}): EditorModel => ({
  quote: {
    id: QUOTE_ID,
    displayNumber: 'Q-2036',
    approvedAt: '2026-09-21T14:14:00Z',
    approvedTotal: 357.28,
    currency: 'USD',
  },
  requestId: REQUEST_ID,
  customer: {
    name: 'Sofia Martinez',
    phone: '(512) 555-0142',
    email: 'sofia@example.com',
    address: '1842 Oak Street, Austin, TX',
  },
  accessNote: null,
  assessment: {
    id: 'a-1',
    technicianName: 'Carlos Rivera',
    diagnosis: 'Failed P-trap',
    photos: [{ id: 'p-1' }],
  },
  workOrder: null,
  values: {
    title: 'Kitchen sink leak repair',
    jobType: 'one_time',
    serviceCategoryId: 'cat-1',
    branchId: 'br-1',
    priority: 'high',
    estimatedDurationMinutes: 120,
    skillIds: [],
    tasks: [{ label: 'Confirm shutoff' }, { label: 'Replace trap' }, { label: 'Test drainage' }],
    materials: [
      {
        quoteLineId: 'ql-1',
        catalogItemId: null,
        description: 'P-trap assembly',
        quantity: 1,
        unit: 'ea',
        source: 'truck_stock',
      },
    ],
    instructions: null,
    preferredDate: null,
    arrivalWindow: 'any',
    recurrence: null,
    communication: {
      notifyCustomerWhenScheduled: true,
      sendTechnicianDetails: true,
      sendArrivalReminder: true,
    },
  },
  options: {
    branches: [{ id: 'br-1', name: 'Austin Central', timezone: 'America/Chicago' }],
    categories: [
      { id: 'cat-1', name: 'Plumbing' },
      { id: 'cat-2', name: 'Electrical' },
    ],
    skills: [{ id: 'sk-1', name: 'Leak repair' }],
  },
  organizationTimezone: 'UTC',
  ...patch,
});

describe('Work order editor page', { timeout: 20_000 }, () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: WorkOrderEditor;

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.body.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const byLabel = (label: string): HTMLButtonElement =>
    document.body.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  const taskInputs = (): string[] =>
    Array.from(document.body.querySelectorAll<HTMLInputElement>('[data-task-input]')).map(
      (input) => input.value,
    );
  const quote = (method: string, path = ''): TestRequest =>
    httpTesting.expectOne(
      (r) => r.method === method && r.url === `${API}/quotes/${QUOTE_ID}/work-order${path}`,
    );
  /** The next request matching `url`, once the page has rendered what triggers it. */
  const next = (match: (r: TestRequest['request']) => boolean): Promise<TestRequest> =>
    vi.waitFor(
      () => {
        harness.detectChanges();
        return httpTesting.expectOne(match);
      },
      { timeout: 2000, interval: 50 },
    );
  const templatesCall = (): Promise<TestRequest> =>
    next((r) => r.url === `${API}/checklist-templates`);
  const dialog = (): HTMLElement | null =>
    document.querySelector('[role="alertdialog"][aria-modal]');
  const type = async (element: HTMLInputElement, value: string): Promise<void> => {
    element.value = value;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  };
  const drag = async (from: number, to: number): Promise<void> => {
    const rows = Array.from(document.body.querySelectorAll<HTMLElement>('.task'));
    const fire = (target: Element, name: string): void => {
      const event = Object.assign(new Event(name, { bubbles: true, cancelable: true }), {
        dataTransfer: null,
      });
      target.dispatchEvent(event);
    };
    fire(rows[from].querySelector('.task__handle')!, 'dragstart');
    fire(rows[to], 'dragover');
    fire(rows[to], 'drop');
    await settle();
  };

  async function setup(
    role: string,
    response: EditorModel | { status: number; code?: string } | null = editorModel(),
  ): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'quotes/:quoteId/work-order',
            component: WorkOrderEditor,
            canDeactivate: [workOrderEditorUnsavedChangesGuard],
          },
          { path: 'quotes/:quoteId', component: Stub },
          { path: 'jobs/:id', component: Stub },
          { path: 'requests', component: Stub },
          { path: 'auth/sign-in', component: Stub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API}/sessions/current`).flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    TestBed.inject(Router).setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl(`/quotes/${QUOTE_ID}/work-order`, WorkOrderEditor);
    if (response !== null) {
      const request = quote('GET');
      if ('status' in response) {
        request.flush(
          { title: 'x', code: response.code },
          { status: response.status, statusText: 'Error' },
        );
      } else {
        request.flush(response);
        httpTesting
          .expectOne(`${API}/service-requests/${REQUEST_ID}/assessments/a-1/photos/p-1`)
          .flush(new Blob(['x']));
      }
    }
    await settle();
  }

  afterEach(() => httpTesting?.verify());

  it.each<[string, EditorModel | { status: number; code?: string } | null, string]>([
    ['accounting', null, WO_FORBIDDEN_MESSAGE],
    ['technician', null, WO_FORBIDDEN_MESSAGE],
    ['owner', { status: 409, code: 'quote_not_approved' }, WO_NOT_APPROVED_MESSAGE],
    [
      'dispatcher',
      { status: 409, code: 'customer_required' },
      'Link a customer and property to this request before creating a work order.',
    ],
  ])(
    '%s sees a state instead of the editor (create-work-order FR-02, FR-11, AC-11, AC-12, AC-20)',
    async (role, response, message) => {
      await setup(role, response);

      expect(text()).toContain(message);
      expect(button('Save draft')).toBeUndefined();
      if (response !== null) {
        expect(button('Back to quote') ?? document.querySelector('a')).toBeDefined();
      }
      // `verify()` proves no request at all for roles without Manage.
    },
  );

  it('renders the locked prefill with summary counts, saves one request at a time and handles 409/400, then creates and navigates (FR-02, FR-07, FR-08, BR-20, AC-02, AC-03, AC-08, AC-23)', async () => {
    await setup('operations_manager');

    // Locked context, prefill and summary (AC-02, AC-03).
    expect(text()).toContain('From quote #Q-2036');
    expect(text()).toContain('Quote approved');
    expect(text()).toContain('Approved Sep 21, 2026 at 2:14 PM');
    expect(text()).toContain('$357.28');
    expect(text()).toContain('Failed P-trap');
    expect(text()).toContain('No access note.');
    expect(document.body.querySelectorAll('[aria-label="Locked, from quote"]')).toHaveLength(3);
    expect(document.body.querySelector<HTMLInputElement>('#wo-title')!.value).toBe(
      'Kitchen sink leak repair',
    );
    expect(text()).toMatch(/Tasks3Planned materials1Estimated duration2h/);
    expect(text()).toContain('$357.28 (#Q-2036)');
    expect(text()).toContain('Pricing remains linked to the approved quote.');
    expect(text()).toContain('Does not repeat');
    expect(button('Check availability')!.disabled).toBe(true);
    expect(text()).not.toContain('Unsaved changes');
    expect(text()).not.toContain('Draft saved');

    // An edit shows the unsaved state; Save sends once with a null token and no prices (BR-16, BR-17).
    await type(document.body.querySelector<HTMLInputElement>('#wo-title')!, 'Sink repair');
    expect(text()).toContain('Unsaved changes');
    button('Save draft')!.click();
    await settle();
    expect(button('Save draft')!.disabled).toBe(true);
    expect(button('Create work order')!.disabled).toBe(true);
    expect(button('Cancel')!.disabled).toBe(true);
    page.saveDraft();
    page.create();
    const put = quote('PUT', '/draft');
    expect(put.request.body).toMatchObject({
      title: 'Sink repair',
      updatedAt: null,
      arrivalWindow: 'any',
      recurrence: null,
      tasks: [{ label: 'Confirm shutoff' }, { label: 'Replace trap' }, { label: 'Test drainage' }],
      materials: [{ quoteLineId: 'ql-1', description: 'P-trap assembly', quantity: 1 }],
    });
    expect(JSON.stringify(put.request.body)).not.toMatch(/price|cost|tax|total/i);
    put.flush({ title: 'x', code: 'work_order_changed' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text()).toContain(WO_CHANGED_MESSAGE);
    expect(button('Reload')).toBeDefined();
    expect(page.dirty()).toBe(true);

    // 400 field errors are inline and move focus to the first invalid field.
    button('Save draft')!.click();
    quote('PUT', '/draft').flush(
      {
        errors: {
          title: ['Title must be 160 characters or fewer.'],
          'tasks[1].label': ['Enter a task.'],
          'materials[0].quantity': ['Enter a quantity greater than 0.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Title must be 160 characters or fewer.');
    // Index-based server keys land on the row of that index (rows are keyed by uid).
    const rows = Array.from(document.body.querySelectorAll('.task'));
    expect(rows.map((row) => row.querySelector('.task__error')?.textContent)).toEqual([
      undefined,
      'Enter a task.',
      undefined,
    ]);
    expect(document.body.querySelector('tbody tr')?.textContent).toContain(
      'Enter a quantity greater than 0.',
    );
    expect(document.activeElement?.id).toBe('wo-title');

    // A successful save shows "Draft saved"; create then sends the saved token and navigates.
    button('Save draft')!.click();
    quote('PUT', '/draft').flush(
      editorModel({
        workOrder: {
          id: 'wo-1',
          displayNumber: 'WO-7',
          status: 'draft',
          updatedAt: UPDATED_AT,
        },
      }),
      { status: 201, statusText: 'Created' },
    );
    await settle();
    expect(text()).toContain('Draft saved');
    expect(text()).not.toContain('Unsaved changes');
    button('Create work order')!.click();
    const post = quote('POST');
    expect(post.request.body).toMatchObject({ title: 'Sink repair', updatedAt: UPDATED_AT });
    post.flush({ id: 'wo-1', displayNumber: 'WO-7' }, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/jobs/wo-1'));
  });

  it('reorders tasks by drag with a polite announcement and focus, appends templates and refuses an overflow, and saves a template (FR-04, BR-10, BR-11, AC-15, AC-16)', async () => {
    await setup('owner');
    expect(taskInputs()).toEqual(['Confirm shutoff', 'Replace trap', 'Test drainage']);
    expect(document.body.querySelector('[data-move]')).toBeNull();

    // Drag reorders, announces politely and focuses the moved row's input.
    await drag(0, 2);
    expect(taskInputs()).toEqual(['Replace trap', 'Test drainage', 'Confirm shutoff']);
    expect(document.body.querySelector('[role="status"]')?.textContent).toBe(
      'Confirm shutoff moved to position 3 of 3',
    );
    await vi.waitFor(() =>
      expect((document.activeElement as HTMLInputElement).value).toBe('Confirm shutoff'),
    );
    await drag(2, 0);
    expect(taskInputs()).toEqual(['Confirm shutoff', 'Replace trap', 'Test drainage']);

    // Add task appends an empty row and focuses it.
    button('Add task')!.click();
    await settle();
    expect(taskInputs()).toHaveLength(4);
    await vi.waitFor(() => expect((document.activeElement as HTMLInputElement).value).toBe(''));

    // Templates: selected category first, apply appends after the existing tasks.
    button('Use checklist template')!.click();
    (await templatesCall()).flush([
      { id: 't-2', name: 'Zeta', serviceCategoryId: 'cat-2', items: [{ label: 'Z1' }] },
      {
        id: 't-1',
        name: 'Alpha',
        serviceCategoryId: 'cat-1',
        items: [{ label: 'A1' }, { label: 'A2' }],
      },
    ]);
    await settle();
    const names = Array.from(document.querySelectorAll('.templates__name')).map(
      (element) => element.textContent,
    );
    expect(names).toEqual(['Alpha', 'Zeta']);
    expect(text()).toContain('2 tasks');
    document.querySelector<HTMLInputElement>('.templates__item input')!.click();
    await settle();
    button('Add tasks')!.click();
    await settle();
    expect(taskInputs().slice(-2)).toEqual(['A1', 'A2']);
    expect(taskInputs()).toHaveLength(6);

    // Overflow: nothing is appended and the limit message shows.
    page.update({
      tasks: Array.from({ length: 49 }, (_, index) => ({ uid: `u-${index}`, label: `T${index}` })),
    });
    button('Use checklist template')!.click();
    (await templatesCall()).flush([
      {
        id: 't-1',
        name: 'Alpha',
        serviceCategoryId: 'cat-1',
        items: [{ label: 'A1' }, { label: 'A2' }],
      },
    ]);
    await settle();
    document.querySelector<HTMLInputElement>('.templates__item input')!.click();
    await settle();
    button('Add tasks')!.click();
    await settle();
    expect(text()).toContain('A work order can have up to 50 tasks.');
    expect(page.form()!.tasks).toHaveLength(49);

    // Save current tasks as a template: duplicate name inline, then success toast.
    await type(document.querySelector<HTMLInputElement>('#wo-template-name')!, 'Alpha');
    button('Save current tasks as template')!.click();
    const body = httpTesting.expectOne(`${API}/checklist-templates`);
    expect(body.request.body).toMatchObject({ name: 'Alpha', serviceCategoryId: 'cat-1' });
    body.flush(
      { errors: { name: ['A template with this name already exists.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('A template with this name already exists.');
    await type(document.querySelector<HTMLInputElement>('#wo-template-name')!, 'Beta');
    button('Save current tasks as template')!.click();
    httpTesting
      .expectOne(`${API}/checklist-templates`)
      .flush(
        { id: 't-3', name: 'Beta', serviceCategoryId: 'cat-1', items: [] },
        { status: 201, statusText: 'Created' },
      );
    await settle();
    expect(text()).toContain('Template saved.');
  });

  it('adds, edits and removes planned materials from the catalog or manually, never showing prices (FR-05, BR-12, AC-17)', async () => {
    await setup('owner');
    expect(text()).toContain('Technician will record actual quantities used.');
    expect(document.body.querySelector('tbody')?.textContent).toContain('Planned');

    // Catalog product prefills the description; only active products are requested.
    button('Add material')!.click();
    const search = await next(
      (r) =>
        r.url === `${API}/catalog-items` &&
        r.params.get('type') === 'product' &&
        r.params.get('status') === 'active',
    );
    search.flush({
      items: [
        {
          id: 'item-1',
          type: 'product',
          name: 'Shutoff valve',
          description: null,
          unitCost: 4,
          unitPrice: 9,
          estimatedMarginPercent: null,
          isTaxable: true,
          isActive: true,
          hasImage: false,
          updatedAt: UPDATED_AT,
        },
      ],
      page: 1,
      pageSize: 10,
      totalCount: 1,
    });
    await settle();
    document.querySelector<HTMLButtonElement>('.material__product')!.click();
    await settle();
    expect(document.querySelector<HTMLInputElement>('#wo-material-description')!.value).toBe(
      'Shutoff valve',
    );
    await type(document.querySelector<HTMLInputElement>('#wo-material-quantity')!, '');
    button('Save material')!.click();
    await settle();
    expect(text()).toContain('Enter a quantity greater than 0.');
    await type(document.querySelector<HTMLInputElement>('#wo-material-quantity')!, '2');
    button('Save material')!.click();
    await settle();
    expect(page.form()!.materials.map((m) => [m.description, m.quantity, m.catalogItemId])).toEqual(
      [
        ['P-trap assembly', 1, null],
        ['Shutoff valve', 2, 'item-1'],
      ],
    );
    expect(text()).toMatch(/Planned materials2/);
    expect(text()).not.toMatch(/\$9/);

    // Edit keeps the quote link; remove updates the count.
    byLabel('Edit P-trap assembly').click();
    (await next((r) => r.url === `${API}/catalog-items`)).flush({
      items: [],
      page: 1,
      pageSize: 10,
      totalCount: 0,
    });
    await settle();
    expect(text()).toContain('No products found.');
    await type(document.querySelector<HTMLInputElement>('#wo-material-quantity')!, '3');
    button('Save material')!.click();
    await settle();
    expect(page.form()!.materials[0]).toMatchObject({ quoteLineId: 'ql-1', quantity: 3 });
    byLabel('Remove Shutoff valve').click();
    await settle();
    expect(page.form()!.materials).toHaveLength(1);
  });

  it('asks before discarding unsaved changes on Cancel and on any navigation (FR-13, BR-23, AC-23)', async () => {
    await setup('owner');

    await type(document.body.querySelector<HTMLInputElement>('#wo-title')!, 'Changed');

    // Cancel with edits asks first: Keep editing stays; Discard leaves without saving.
    button('Cancel')!.click();
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Discard unsaved changes?'));
    button('Keep editing')!.click();
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}/work-order`);

    // Any other navigation (sidebar link, browser back) uses the same prompt.
    void TestBed.inject(Router).navigateByUrl('/requests');
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Discard unsaved changes?'));
    button('Discard changes')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/requests'));
  });
});
