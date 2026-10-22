import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { CustomerDrawer } from '../../components/customer-drawer/customer-drawer';
import { PropertyDrawer } from '../../components/property-drawer/property-drawer';
import { RelatedListTab } from '../../components/related-list-tab/related-list-tab';
import { NewRequestDrawer } from '../../../requests/components/new-request-drawer/new-request-drawer';
import {
  CustomerOption,
  RequestDetail,
  RequestOptions,
} from '../../../requests/models/requests.model';
import { PropertyFormValue } from '../../components/property-drawer/property-drawer.validators';
import {
  CustomerDetail as CustomerDrawerDetail,
  CustomerOverview,
  NoteItem,
  PropertyItem,
} from '../../models/customer.model';
import { CustomerDetail } from './customer-detail';

const API = 'http://api.test';
const ID = '3f2b8c1e-5a4d-4e6f-9a7b-1c2d3e4f5a6b';
const NO_CONTENT = { status: 204, statusText: 'No Content' };
const SERVER_ERROR = { status: 500, statusText: 'Server Error' };

@Component({ template: '<p>stub</p>' })
class Stub {}

const OVERVIEW: CustomerOverview = {
  id: ID,
  type: 'residential',
  displayName: 'Sofia Martinez',
  contact: {
    firstName: 'Sofia',
    lastName: 'Martinez',
    email: 'sofia@example.com',
    phone: '5125557832',
    prefersEmail: true,
    prefersSms: true,
  },
  lifecycle: 'active',
  displayStatus: 'active',
  isActive: true,
  outstandingBalance: 0,
  currency: 'USD',
  timezone: 'America/Chicago',
  summary: {
    totalJobs: 3,
    lifetimeValue: 1142.28,
    customerSince: '2025-03-10T15:00:00Z',
    lastServiceAt: '2026-09-20T15:00:00Z',
  },
  lastInvoice: { number: 'INV-1048', status: 'paid', issueDate: '2026-09-20' },
  tags: [
    { id: 't-2', name: 'Plumbing' },
    { id: 't-1', name: 'VIP' },
  ],
  pinnedNote: 'Prefers morning appointments.',
};
const property = (over: Partial<PropertyItem>): PropertyItem => ({
  id: 'p-1',
  name: 'Home',
  isPrimary: false,
  isActive: true,
  addressLine1: '742 Maple Ave',
  addressLine2: null,
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78704',
  branch: { id: 'b-1', name: 'Austin Central' },
  serviceInstructions: 'Use side gate.',
  lastService: { completedAt: '2026-09-20T15:00:00Z', summary: 'Sink repair\nmore' },
  nextAppointment: null,
  ...over,
});
const HOME = property({ id: 'p-1', name: 'Home', isPrimary: true });
const LAKE = property({
  id: 'p-2',
  name: 'Lake house',
  branch: { id: 'b-9', name: 'Dallas' },
  lastService: null,
  serviceInstructions: null,
});
const OLD = property({ id: 'p-3', name: 'Old shop', isActive: false, lastService: null });
const PROPERTIES = { items: [LAKE, OLD, HOME], timezone: 'America/Chicago' };
const BRANCHES = { countryCode: 'US', branches: [{ id: 'b-1', name: 'Austin Central' }] };
const CUSTOMER: CustomerDrawerDetail = {
  id: ID,
  type: 'residential',
  contact: {
    firstName: 'Sofia',
    lastName: 'Martinez',
    email: 'sofia@example.com',
    phone: '5125557832',
    prefersEmail: true,
    prefersSms: false,
  },
  property: {
    addressLine1: '742 Maple Ave',
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78704',
  },
  serviceInstructions: null,
  internalNote: 'Pinned',
  branchId: 'b-1',
  tags: [],
  lifecycle: 'active',
  displayStatus: 'active',
  isActive: true,
};
const note = (n: number): NoteItem => ({
  id: `n-${n}`,
  note: `Note ${n}`,
  authorName: 'Alex Morgan',
  createdAt: '2026-09-20T15:00:00Z',
});
const notesBody = (items: readonly NoteItem[], page: number, totalCount: number) => ({
  items,
  totalCount,
  page,
  pageSize: 10,
  timezone: 'America/Chicago',
});
/** Accounting has no Jobs read: Total jobs is omitted (BR-14). */
const ACCOUNTING_OVERVIEW: CustomerOverview = {
  ...OVERVIEW,
  summary: { customerSince: OVERVIEW.summary.customerSince, lifetimeValue: 1142.28 },
};
const ROLES = [
  { role: 'owner', mutate: true, forbidden: false, jobs: true },
  { role: 'dispatcher', mutate: true, forbidden: false, jobs: true },
  { role: 'operations_manager', mutate: false, forbidden: false, jobs: true },
  { role: 'accounting', mutate: false, forbidden: false, jobs: false },
  { role: 'viewer', mutate: false, forbidden: false, jobs: true },
  { role: 'technician', mutate: false, forbidden: true, jobs: false },
];
const TABS_ALL = ['Overview', 'Requests', 'Quotes', 'Jobs', 'Invoices', 'Activity'];
const CUSTOMER_OPTION: CustomerOption = {
  id: ID,
  displayName: 'Sofia Martinez',
  branchId: 'b-1',
  contacts: [
    { id: 'c-1', name: 'Sofia Martinez', email: 'sofia@example.com', phone: null, isPrimary: true },
  ],
  properties: [
    { id: 'p-1', name: 'Home', addressLine1: '742 Maple Ave', city: 'Austin', isPrimary: true },
  ],
};
const REQUEST_OPTIONS = {
  requestPrefix: 'REQ',
  timezone: 'America/Chicago',
  categories: [],
  assignees: [],
  branches: [],
  technicians: [],
} as unknown as RequestOptions;
const requestsPage = (items: readonly object[], customerTotal = items.length) => ({
  items,
  totalCount: items.length,
  customerTotal,
  page: 1,
  pageSize: 10,
  timezone: 'America/Chicago',
});
const REQUEST_ROW = {
  id: 'r-1',
  number: 'REQ-1048',
  service: 'General services',
  propertyName: 'Home',
  createdAt: '2026-09-20T15:00:00Z',
  status: 'new',
};
const ago = (days: number, hours = 0): string =>
  new Date(Date.now() - days * 86_400_000 - hours * 3_600_000).toISOString();
const event = (id: string, over: object = {}) => ({
  id,
  type: 'customer',
  action: 'customer.updated',
  occurredAt: ago(0, 0.01),
  origin: { kind: 'staff', actorName: 'Alex Morgan' },
  subject: {},
  link: null,
  ...over,
});
const activityBody = (items: readonly object[], nextCursor: string | null = null) => ({
  items,
  availableTypes: ['customer', 'invoices', 'messages', 'payments'],
  nextCursor,
  currency: 'USD',
  timezone: 'America/Chicago',
});

describe('Customer detail page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: CustomerDetail;
  let fixture: ComponentFixture<unknown>;
  let host: HTMLElement;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const flushOverview = (
    regions: {
      notes?: readonly NoteItem[];
      total?: number;
      properties?: object;
      jobs?: boolean;
      overview?: CustomerOverview;
    } = {},
    withDetail = true,
  ): void => {
    if (withDetail) {
      call('GET', `customers/${ID}/detail`).flush(regions.overview ?? OVERVIEW);
    }
    call('GET', `customers/${ID}/properties`).flush(regions.properties ?? PROPERTIES);
    call('GET', `customers/${ID}/recent-work`).flush({
      items: [
        {
          type: 'job',
          id: 'w-1',
          number: 'WO-1048',
          title: 'Kitchen sink',
          status: 'approved_for_billing',
          date: '2026-09-20T15:00:00Z',
          technicianName: 'Carlos Rivera',
          amount: 357.28,
        },
        {
          type: 'quote',
          id: 'q-1',
          number: 'Q-2036',
          title: 'Kitchen sink',
          status: 'sent',
          date: '2026-09-19T15:00:00Z',
          technicianName: null,
          amount: 357.29,
        },
        {
          type: 'request',
          id: 'r-1',
          number: 'REQ-1048',
          title: 'Kitchen sink',
          status: 'quoted',
          date: '2026-09-18T15:00:00Z',
          technicianName: null,
          amount: null,
        },
      ],
      currency: 'USD',
      timezone: 'America/Chicago',
    });
    if (regions.jobs ?? true) {
      call('GET', `customers/${ID}/upcoming-appointments`).flush({
        items: [],
        timezone: 'America/Chicago',
      });
    }
    call('GET', `customers/${ID}/notes`).flush(
      notesBody(regions.notes ?? [], 1, regions.total ?? regions.notes?.length ?? 0),
    );
  };
  const flushOptions = (): void => {
    call('GET', 'customers/branch-options').flush(BRANCHES);
    call('GET', 'customer-tags').flush([{ id: 't-1', name: 'VIP' }]);
  };

  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLElement | undefined =>
    Array.from(host.querySelectorAll<HTMLElement>('button, a')).find(
      (b) => b.textContent?.trim() === label,
    );
  const byLabel = (label: string): HTMLElement | null =>
    host.querySelector<HTMLElement>(`[aria-label="${label}"]`);
  const pageInjector = () => fixture.debugElement.query(By.directive(CustomerDetail)).injector;
  const confirmations = (): Confirmation[] => {
    const captured: Confirmation[] = [];
    vi.spyOn(pageInjector().get(ConfirmationService), 'confirm').mockImplementation(
      (confirmation) => {
        captured.push(confirmation);
        return undefined as never;
      },
    );
    return captured;
  };
  const toasts = (): string[] => {
    const summaries: string[] = [];
    vi.spyOn(pageInjector().get(MessageService), 'add').mockImplementation(
      (message) => void summaries.push(message.summary ?? ''),
    );
    return summaries;
  };
  const propertyDrawer = (): PropertyDrawer =>
    fixture.debugElement.query(By.directive(PropertyDrawer)).componentInstance;
  const customerDrawer = (): CustomerDrawer =>
    fixture.debugElement.query(By.directive(CustomerDrawer)).componentInstance;

  async function open(roleCode: string, url = `/customers/${ID}`): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'auth/sign-in', component: Stub },
          { path: 'customers', component: Stub },
          { path: 'coming-soon/:module', component: Stub },
          { path: 'customers/:customerId', component: CustomerDetail },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    call('GET', 'sessions/current').flush({
      user: {
        id: 'u-1',
        firstName: 'Alex',
        lastName: 'Morgan',
        email: 'alex@acme.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: roleCode },
    });
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl(url, CustomerDetail);
    fixture = harness.fixture;
    host = fixture.nativeElement as HTMLElement;
  }

  afterEach(() => httpTesting.verify());

  it.each(ROLES)(
    'applies role UX per BR-02/BR-03 for $role: Technician gets the forbidden state with no requests; read roles see data without mutation controls (AC-01 to AC-03)',
    async ({ role, mutate, forbidden, jobs }) => {
      await open(role);
      if (forbidden) {
        await settle();
        expect(text()).toContain("You don't have access to customers.");
        expect(host.querySelector('[role="tablist"]')).toBeNull();
        expect(host.querySelector('app-customer-drawer')).toBeNull();
      } else {
        flushOverview({ jobs, overview: jobs ? OVERVIEW : ACCOUNTING_OVERVIEW });
        if (mutate) {
          flushOptions();
        }
        await settle();
        // BR-03/BR-14: the Jobs tab, Upcoming appointments, Total jobs and property history need Jobs read.
        expect(
          Array.from(host.querySelectorAll('[role="tab"]')).map((tab) => tab.textContent?.trim()),
        ).toEqual(jobs ? TABS_ALL : TABS_ALL.filter((label) => label !== 'Jobs'));
        expect(
          ['Upcoming appointments', 'Total jobs', 'Last service'].map((label) =>
            text().includes(label),
          ),
        ).toEqual([jobs, jobs, jobs]);
        expect(text()).toContain('Lifetime value');
        const controls = [
          'Edit customer',
          'Create',
          'Add property',
          'Edit',
          'Schedule a job',
          'Add note',
        ].map((label) => button(label) !== undefined);
        expect(controls.every((visible) => visible === mutate)).toBe(true);
        expect(
          ['More actions', 'Edit contact details', 'Edit internal notes', 'Edit tags'].map(
            (label) => byLabel(label) !== null,
          ),
        ).toEqual([mutate, mutate, mutate, mutate]);
        expect(byLabel('Actions for Lake house') !== null).toBe(mutate);
        expect(host.querySelector('#customer-detail-note') !== null).toBe(mutate);
        expect(button('View property')).toBeDefined();
        // BR-03/BR-04/BR-23/BR-11/BR-12 render.
        expect(text()).toContain('SM');
        expect(text()).toContain('Residential');
        expect(text()).toContain('(512) 555-7832');
        expect(text()).toContain('Email & SMS');
        expect(text()).toContain('$0.00');
        expect(text()).toContain('Properties (2)');
        expect(text()).toContain('Archived properties (1)');
        expect(text()).toContain('Plumbing');
        expect(text()).toContain('Since Mar 2025');
        expect(text()).toContain('INV-1048');
        expect(text()).toContain('Approved for billing');
        const names = Array.from(host.querySelectorAll('.properties__name')).map((el) =>
          el.textContent?.trim(),
        );
        expect(names[0]).toContain('Home');
        expect(names[0]).toContain('Primary');
        expect(text().includes('Sep 20, 2026 · Sink repair')).toBe(jobs);
        // Quote links open the read-only quote page; request links the Requests panel (quote-builder BR-30).
        expect(host.querySelector('a[href="/quotes/q-1"]')).not.toBeNull();
        expect(host.querySelector('a[href="/requests?request=r-1"]')).not.toBeNull();
      }
    },
    20_000,
  );

  it('syncs the tab with ?tab=, shows the new Activity shape and routes the Create menu (BR-01, AC-01, AC-14)', async () => {
    await open('owner', `/customers/${ID}?tab=activity`);
    // Only the header and Activity load; Overview regions wait until they are shown.
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    flushOptions();
    call('GET', `customers/${ID}/activity`).flush(
      activityBody([
        event('a-1', {
          action: 'property.created',
          occurredAt: '2026-09-20T15:00:00Z',
          origin: { kind: 'system' },
          subject: { propertyName: 'Lake house' },
        }),
        event('a-2', { occurredAt: '2026-09-19T15:00:00Z' }),
      ]),
    );
    await settle();
    const selected = () =>
      host.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim();
    expect(selected()).toBe('Activity');
    expect(text()).toContain('Property added: Lake house');
    expect(text()).toContain('Sep 20, 2026');
    expect(text()).toContain('10:00 AM · System');
    expect(text()).toContain('Customer details updated');

    button('Quotes')!.click();
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/customers/${ID}?tab=quotes`);
    expect(selected()).toBe('Quotes');
    call('GET', `customers/${ID}/quotes`).flush({ ...requestsPage([]), currency: 'USD' });
    await settle();
    expect(text()).toContain('No quotes yet');
    expect(text()).not.toContain('coming soon');

    // An unknown tab shows Overview and loads its regions once.
    await harness.navigateByUrl(`/customers/${ID}?tab=nope`);
    flushOverview({}, false);
    await settle();
    expect(selected()).toBe('Overview');

    button('Create')!.click();
    await settle();
    expect(page.menuModel().map((item) => item.label)).toEqual([
      'Property',
      'Request',
      'Quote',
      'Job',
      'Invoice',
    ]);
    page.menuModel()[3].command!({});
    await settle();
    expect(TestBed.inject(Router).url).toBe('/coming-soon/work-orders');
  }, 20_000);

  it('shows not-found for malformed and unknown ids and isolates region failures with Retry (AC-24)', async () => {
    await open('viewer', '/customers/not-a-uuid');
    await settle();
    expect(text()).toContain('Customer not found');
    expect(text()).toContain("This customer doesn't exist or you don't have access to it.");
    expect(button('Back to customers')!.getAttribute('href')).toBe('/customers');

    await harness.navigateByUrl(`/customers/${ID}`);
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    call('GET', `customers/${ID}/properties`).flush(null, SERVER_ERROR);
    call('GET', `customers/${ID}/recent-work`).flush({
      items: [],
      currency: 'USD',
      timezone: 'UTC',
    });
    call('GET', `customers/${ID}/upcoming-appointments`).flush({ items: [], timezone: 'UTC' });
    call('GET', `customers/${ID}/notes`).flush(notesBody([], 1, 0));
    await settle();
    expect(text()).toContain("We couldn't load properties.");
    expect(text()).toContain('No work yet');
    expect(text()).toContain('No upcoming appointments');
    expect(text()).toContain('No notes yet');
    expect(text()).toContain('Prefers morning appointments.');
    button('Retry')!.click();
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();
    expect(text()).toContain('Properties (2)');

    // A 404 from any read turns the page into the not-found state.
    page.loadOverview();
    call('GET', `customers/${ID}/detail`).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain('Customer not found');
  });

  it('validates the property drawer, handles the branch option and maps server errors (AC-06, AC-07, BR-22)', async () => {
    await open('dispatcher');
    flushOverview();
    flushOptions();
    await settle();
    const dialogs = confirmations();

    button('Add property')!.click();
    await settle();
    call('GET', `customers/${ID}`).flush(CUSTOMER);
    await settle();
    const drawer = propertyDrawer();
    expect(host.querySelector('.property-drawer__title')?.textContent?.trim()).toBe('Add property');
    // New property default: the customer's branch when it is among the options.
    expect(drawer.form().branchId).toBe('b-1');

    const valid: PropertyFormValue = {
      name: 'Warehouse',
      addressLine1: '1 Dock Rd',
      addressLine2: '',
      city: 'Austin',
      stateRegion: 'TX',
      postalCode: '78701',
      branchId: 'b-1',
      serviceInstructions: '',
    };
    const invalid: [Partial<PropertyFormValue>, keyof PropertyFormValue, string][] = [
      [{ name: ' ' }, 'name', 'Enter a property name.'],
      [{ name: 'x'.repeat(141) }, 'name', 'Use 140 characters or fewer.'],
      [{ addressLine1: '' }, 'addressLine1', 'Enter an address.'],
      [{ addressLine1: 'x'.repeat(181) }, 'addressLine1', 'Use 180 characters or fewer.'],
      [{ addressLine2: 'x'.repeat(181) }, 'addressLine2', 'Use 180 characters or fewer.'],
      [{ city: '' }, 'city', 'Enter a city.'],
      [{ city: 'x'.repeat(101) }, 'city', 'Use 100 characters or fewer.'],
      [{ stateRegion: 'ZZ' }, 'stateRegion', 'Choose a valid state.'],
      [{ postalCode: '123' }, 'postalCode', 'Enter a valid ZIP code.'],
      [{ branchId: null }, 'branchId', 'Choose a branch.'],
      [
        { serviceInstructions: 'x'.repeat(2001) },
        'serviceInstructions',
        'Use 2,000 characters or fewer.',
      ],
    ];
    for (const [change, field, message] of invalid) {
      drawer.form.set({ ...valid, ...change });
      drawer.submit();
      expect(drawer.fieldErrors()[field]).toBe(message);
    }
    // `verify()` fails if any invalid submit reached the API.

    drawer.form.set({ ...valid, name: ' Warehouse ', serviceInstructions: ' ' });
    drawer.submit();
    const create = call('POST', `customers/${ID}/properties`);
    expect(create.request.body).toEqual({
      name: 'Warehouse',
      addressLine1: '1 Dock Rd',
      addressLine2: null,
      city: 'Austin',
      stateRegion: 'TX',
      postalCode: '78701',
      branchId: 'b-1',
      serviceInstructions: null,
    });
    expect(drawer.submitting()).toBe(true);
    create.flush(
      { errors: { branchId: ['Choose a branch you have access to.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(drawer.fieldErrors().branchId).toBe('Choose a branch you have access to.');
    expect(drawer.submitting()).toBe(false);

    // Edit with a branch outside the options: it stays the selected option with its name and an
    // unchanged branch is sent as is. A dirty close asks first.
    page.onPropertyDrawerClosed();
    await settle();
    page.editProperty(LAKE);
    await settle();
    call('GET', `customers/${ID}/properties/p-2`).flush(LAKE);
    await settle();
    expect(drawer.branchOptions()).toEqual([
      { id: 'b-1', name: 'Austin Central' },
      { id: 'b-9', name: 'Dallas' },
    ]);
    expect(drawer.form().branchId).toBe('b-9');
    expect(host.querySelector('.property-drawer__title')?.textContent?.trim()).toBe(
      'Edit property',
    );
    drawer.patch({ name: 'Lake home' }, 'name');
    drawer.requestClose();
    expect(dialogs[0]).toMatchObject({ key: 'discard-changes' });
    expect(dialogs[0].message).toContain('this property');
    drawer.submit();
    const update = call('PUT', `customers/${ID}/properties/p-2`);
    expect(update.request.body.branchId).toBe('b-9');
    update.flush({ ...LAKE, name: 'Lake home' });
    await settle();
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();
    expect(page.propertyDrawerOpen()).toBe(false);

    // Read-only roles open View property with no inputs enabled (covered by role UX); a 404 closes it.
    page.viewProperty(LAKE);
    await settle();
    call('GET', `customers/${ID}/properties/p-2`).flush(null, {
      status: 404,
      statusText: 'Not Found',
    });
    await settle();
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    expect(page.propertyDrawerOpen()).toBe(false);
  }, 30_000);

  it('lists notes newest first with Show more and appends validated notes (BR-16, AC-17)', async () => {
    await open('owner');
    const first = Array.from({ length: 10 }, (_, i) => note(12 - i));
    flushOverview({ notes: first, total: 12 });
    flushOptions();
    await settle();
    const messages = toasts();
    expect(host.querySelectorAll('.notes__entry').length).toBe(10);
    expect(text()).toContain('Alex Morgan · Sep 20, 2026 · 10:00 AM');

    button('Show more')!.click();
    const more = httpTesting.expectOne(
      (r) => r.url === `${API}/customers/${ID}/notes` && r.params.get('page') === '2',
    );
    more.flush(notesBody([note(2), note(1)], 2, 12));
    await settle();
    expect(host.querySelectorAll('.notes__entry').length).toBe(12);
    expect(button('Show more')).toBeUndefined();

    // Empty and 2,001-character notes never reach the API.
    for (const [value, message] of [
      ['   ', 'Enter a note.'],
      ['x'.repeat(2001), 'Use 2,000 characters or fewer.'],
    ]) {
      page.onNoteText(value);
      page.addNote();
      await settle();
      expect(host.querySelector('.notes__error')?.textContent?.trim()).toBe(message);
    }

    page.onNoteText('  Gate code 4411  ');
    button('Add note')!.click();
    await settle();
    expect(button('Add note')!.hasAttribute('disabled')).toBe(true);
    page.addNote(); // double submission is ignored while pending
    const created = call('POST', `customers/${ID}/notes`);
    expect(created.request.body).toEqual({ note: 'Gate code 4411' });
    created.flush({ ...note(13), note: 'Gate code 4411' });
    await settle();
    expect(host.querySelector('.notes__entry')?.textContent).toContain('Gate code 4411');
    expect(host.querySelectorAll('.notes__entry').length).toBe(13);
    expect(host.querySelector<HTMLTextAreaElement>('#customer-detail-note')!.value).toBe('');
    expect(messages).toContain('Note added.');

    // A failed add keeps the text and shows the toast.
    page.onNoteText('Keep me');
    page.addNote();
    call('POST', `customers/${ID}/notes`).flush(null, SERVER_ERROR);
    await settle();
    expect(page.noteText()).toBe('Keep me');
    expect(messages).toContain("We couldn't add the note. Try again.");
  }, 20_000);

  it('opens the customer drawer from the pencils with focus, archives and reactivates the customer and maps property 409s (BR-08, BR-10, AC-13)', async () => {
    await open('owner');
    flushOverview();
    flushOptions();
    await settle();
    const dialogs = confirmations();
    const messages = toasts();

    byLabel('Edit internal notes')!.click();
    await settle();
    expect(page.customerDrawerOpen()).toBe(true);
    call('GET', `customers/${ID}`).flush(CUSTOMER);
    await settle();
    expect(customerDrawer().focusField()).toBe('internalNote');
    expect(document.activeElement?.id).toBe('customer-note');
    expect(host.querySelector('.customer-drawer__title')?.textContent?.trim()).toBe(
      'Edit customer',
    );
    // Saving refreshes header, side cards and properties.
    page.onCustomerDrawerClosed();
    page.onCustomerSaved();
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();

    // Archive asks first, stays on the page and flips the menu.
    byLabel('More actions')!.click();
    await settle();
    expect(page.menuModel().map((item) => item.label)).toEqual(['Archive customer']);
    page.menuModel()[0].command!({});
    expect(dialogs[0].header).toBe('Archive Sofia Martinez?');
    dialogs[0].accept!();
    call('POST', `customers/${ID}/archive`).flush(null, NO_CONTENT);
    call('GET', `customers/${ID}/detail`).flush({
      ...OVERVIEW,
      lifecycle: 'archived',
      displayStatus: 'archived',
      isActive: false,
    });
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();
    expect(messages).toContain('Customer archived.');
    expect(text()).toContain('Archived');
    byLabel('More actions')!.click();
    expect(page.menuModel().map((item) => item.label)).toEqual(['Reactivate customer']);
    page.menuModel()[0].command!({});
    call('POST', `customers/${ID}/reactivate`).flush(
      { errors: {} },
      { status: 409, statusText: 'Conflict' },
    );
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    await settle();
    expect(messages).toContain('This customer is already active.');

    // Property actions: Set as primary 409 shows the BR-07 message and refreshes the list.
    page.openPropertyMenu(new Event('click'), LAKE);
    expect(page.menuModel().map((item) => item.label)).toEqual(['Set as primary', 'Archive']);
    page.menuModel()[0].command!({});
    call('POST', `customers/${ID}/properties/p-2/set-primary`).flush(
      { errors: {} },
      { status: 409, statusText: 'Conflict' },
    );
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();
    expect(messages).toContain(
      'The primary property was changed by someone else. Refresh and try again.',
    );
    page.openPropertyMenu(new Event('click'), OLD);
    expect(page.menuModel().map((item) => item.label)).toEqual(['Reactivate']);
    page.openPropertyMenu(new Event('click'), LAKE);
    page.menuModel()[1].command!({});
    expect(dialogs[1].header).toBe('Archive Lake house?');
    dialogs[1].accept!();
    call('POST', `customers/${ID}/properties/p-2/archive`).flush(null, NO_CONTENT);
    call('GET', `customers/${ID}/properties`).flush(PROPERTIES);
    await settle();
    expect(messages).toContain('Property archived.');
  }, 30_000);

  it('loads each related list on first activation, keeps per-tab filters while switching and omits unreadable tabs (BR-03, AC-01, AC-06, AC-07, AC-09)', async () => {
    await open('owner');
    flushOverview();
    flushOptions();
    await settle();
    const lists = (): RelatedListTab[] =>
      fixture.debugElement
        .queryAll(By.directive(RelatedListTab))
        .map((node) => node.componentInstance);
    expect(lists()).toHaveLength(0);

    button('Requests')!.click();
    await settle();
    const first = call('GET', `customers/${ID}/requests`);
    expect(first.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    expect(first.request.params.get('pageSize')).toBe('10');
    first.flush(requestsPage([REQUEST_ROW], 3));
    await settle();
    expect(text()).toContain('Requests (3)');
    expect(text()).toContain('Showing 1–1 of 1 requests');
    const row = host.querySelector<HTMLAnchorElement>('a[href="/requests?request=r-1"]')!;
    expect(row.getAttribute('aria-label')).toBe('REQ-1048, New');
    expect(button('New request')).toBeDefined();

    // Status filter returns to page 1; another tab loads on its first activation only.
    lists()[0].page.set(2);
    lists()[0].onStatus('quoted');
    const filtered = call('GET', `customers/${ID}/requests`);
    expect(filtered.request.params.get('status')).toBe('quoted');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush(requestsPage([], 3));
    await settle();
    expect(text()).toContain('No requests match your filters.');

    button('Invoices')!.click();
    await settle();
    call('GET', `customers/${ID}/invoices`).flush({
      ...requestsPage([]),
      currency: 'USD',
      summary: { outstanding: 0, overdue: 0, paid: 0 },
    });
    button('Requests')!.click();
    await settle();
    // No new request: the tab stayed mounted with its own filter (AC-07).
    expect(lists()[0].status()).toBe('quoted');
    expect(lists()).toHaveLength(2);
  }, 20_000);

  it('hides the Jobs tab and treats ?tab=jobs as Overview for Accounting (BR-03, AC-09)', async () => {
    await open('accounting', `/customers/${ID}?tab=jobs`);
    flushOverview({ jobs: false, overview: ACCOUNTING_OVERVIEW });
    await settle();
    expect(host.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim()).toBe(
      'Overview',
    );
    expect(button('Jobs')).toBeUndefined();
    expect(text()).toContain('Properties (2)');
  });

  it('renders Activity day groups, loads older blocks without duplicates, resets on filters and keeps events on an older-block error (BR-11 to BR-13, AC-14 to AC-17)', async () => {
    await open('owner', `/customers/${ID}?tab=activity`);
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    flushOptions();
    const payment = event('e-1', {
      type: 'payments',
      action: 'payment.recorded',
      subject: { amount: 890, invoiceNumbers: ['INV-2026-069'] },
      link: { kind: 'invoice', id: 'i-1' },
      origin: { kind: 'staff', actorName: 'Laura Gómez' },
    });
    const message = event('e-2', {
      type: 'messages',
      action: 'portal.message_sent',
      subject: { contactName: 'Raul' },
      origin: { kind: 'customer_portal' },
    });
    const first = call('GET', `customers/${ID}/activity`);
    expect(first.request.params.get('period')).toBe('last_90_days');
    expect(first.request.params.has('type')).toBe(false);
    first.flush(
      activityBody([payment, message, event('e-3', { occurredAt: ago(1, 0) })], 'cursor-1'),
    );
    await settle();
    const days = Array.from(host.querySelectorAll('.activity__day')).map((el) => el.textContent);
    expect(days).toEqual(['Today', 'Yesterday']);
    expect(text()).toContain('Payment received');
    expect(text()).toContain('$890.00 received for INV-2026-069');
    expect(text()).toContain('Customer message');
    expect(text()).toContain('Raul sent a message');
    expect(text()).toContain('Customer portal');
    expect(host.querySelector('a[href="/invoices/i-1"]')).not.toBeNull();
    // Message events have no destination: no link and no chevron (BR-11).
    expect(host.querySelector('[data-event-id="e-2"]')!.tagName).toBe('DIV');

    // Load older: appended without duplicates; focus moves to the first new event; button hides.
    button('Load older activity')!.click();
    const older = call('GET', `customers/${ID}/activity`);
    expect(older.request.params.get('before')).toBe('cursor-1');
    older.flush(activityBody([event('e-3'), event('e-4', { occurredAt: ago(40) })], null));
    await settle();
    expect(host.querySelectorAll('.activity__event')).toHaveLength(4);
    expect((document.activeElement as HTMLElement | null)?.dataset['eventId']).toBe('e-4');
    expect(button('Load older activity')).toBeUndefined();

    // A type change reloads from the newest; a failed older block keeps the loaded events.
    page.onActivityType('invoices');
    const filtered = call('GET', `customers/${ID}/activity`);
    expect(filtered.request.params.get('type')).toBe('invoices');
    expect(filtered.request.params.has('before')).toBe(false);
    filtered.flush(activityBody([event('e-5')], 'cursor-2'));
    await settle();
    expect(host.querySelectorAll('.activity__event')).toHaveLength(1);
    button('Load older activity')!.click();
    call('GET', `customers/${ID}/activity`).flush(null, SERVER_ERROR);
    await settle();
    expect(text()).toContain("We couldn't load older activity.");
    expect(host.querySelectorAll('.activity__event')).toHaveLength(1);
    button('Retry')!.click();
    call('GET', `customers/${ID}/activity`).flush(
      activityBody([event('e-6', { occurredAt: ago(50) })]),
    );
    await settle();
    expect(host.querySelectorAll('.activity__event')).toHaveLength(2);

    // Period change and the empty state.
    page.onActivityPeriod('last_7_days');
    const period = call('GET', `customers/${ID}/activity`);
    expect(period.request.params.get('period')).toBe('last_7_days');
    period.flush(activityBody([]));
    await settle();
    expect(text()).toContain('No activity in this period.');
  }, 30_000);

  it('opens New request in place with the customer locked, refreshes Requests and shows the BR-09 toast when unavailable (BR-09, AC-12, AC-13)', async () => {
    await open('owner');
    flushOverview();
    flushOptions();
    await settle();
    const messages = toasts();
    button('Requests')!.click();
    await settle();
    call('GET', `customers/${ID}/requests`).flush(requestsPage([REQUEST_ROW]));
    await settle();

    // Header action and Create -> Request open the same drawer; options load once.
    button('New request')!.click();
    const lookup = call('GET', 'service-requests/customer-options');
    expect(lookup.request.params.get('customerId')).toBe(ID);
    expect(lookup.request.params.has('search')).toBe(false);
    lookup.flush({ items: [CUSTOMER_OPTION] });
    call('GET', 'service-requests/options').flush(REQUEST_OPTIONS);
    await settle();
    const drawer = (): NewRequestDrawer =>
      fixture.debugElement.query(By.directive(NewRequestDrawer)).componentInstance;
    expect(drawer().open()).toBe(true);
    expect(drawer().lockedCustomer()?.id).toBe(ID);
    expect(drawer().customer()?.id).toBe(ID);
    expect(drawer().contactId()).toBe('c-1');
    expect(drawer().propertyId()).toBe('p-1');
    expect(drawer().dirty()).toBe(false);

    drawer().created.emit({ id: 'r-9' } as RequestDetail);
    call('GET', `customers/${ID}/requests`).flush(requestsPage([REQUEST_ROW]));
    await settle();
    expect(messages).toContain('Request created.');
    expect(drawer().open()).toBe(false);
    expect(TestBed.inject(Router).url).toBe(`/customers/${ID}?tab=requests`);

    // The customer became unavailable: toast, no drawer.
    page.openCreateMenu(new Event('click'));
    page.menuModel().find((item) => item.label === 'Request')!.command!({});
    call('GET', 'service-requests/customer-options').flush({ items: [] });
    await settle();
    expect(messages).toContain("This customer can't receive new requests right now.");
    expect(drawer().open()).toBe(false);

    // Archived customers get no entry point.
    page.loadOverview();
    call('GET', `customers/${ID}/detail`).flush({ ...OVERVIEW, isActive: false });
    await settle();
    page.openCreateMenu(new Event('click'));
    expect(page.menuModel().map((item) => item.label)).toEqual([
      'Property',
      'Quote',
      'Job',
      'Invoice',
    ]);
    expect(button('New request')).toBeUndefined();
  }, 30_000);
});
