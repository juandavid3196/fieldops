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
const ROLES = [
  { role: 'owner', mutate: true, forbidden: false },
  { role: 'dispatcher', mutate: true, forbidden: false },
  { role: 'operations_manager', mutate: false, forbidden: false },
  { role: 'accounting', mutate: false, forbidden: false },
  { role: 'viewer', mutate: false, forbidden: false },
  { role: 'technician', mutate: false, forbidden: true },
];

describe('Customer detail page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: CustomerDetail;
  let fixture: ComponentFixture<unknown>;
  let host: HTMLElement;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const flushOverview = (
    regions: { notes?: readonly NoteItem[]; total?: number; properties?: object } = {},
    withDetail = true,
  ): void => {
    if (withDetail) {
      call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
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
    call('GET', `customers/${ID}/upcoming-appointments`).flush({
      items: [],
      timezone: 'America/Chicago',
    });
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
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
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
    async ({ role, mutate, forbidden }) => {
      await open(role);
      if (forbidden) {
        await settle();
        expect(text()).toContain("You don't have access to customers.");
        expect(host.querySelector('[role="tablist"]')).toBeNull();
        expect(host.querySelector('app-customer-drawer')).toBeNull();
      } else {
        flushOverview();
        if (mutate) {
          flushOptions();
        }
        await settle();
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
        expect(text()).toContain('Sep 20, 2026 · Sink repair');
        // Quote links open the read-only quote page; request links the Requests panel (quote-builder BR-30).
        expect(host.querySelector('a[href="/quotes/q-1"]')).not.toBeNull();
        expect(host.querySelector('a[href="/requests?request=r-1"]')).not.toBeNull();
      }
    },
    20_000,
  );

  it('syncs the tab with ?tab=, makes no request for Coming soon tabs and routes the Create menu (BR-01, BR-18, AC-01, AC-19)', async () => {
    await open('owner', `/customers/${ID}?tab=activity`);
    // Only the header and Activity load; Overview regions wait until they are shown.
    call('GET', `customers/${ID}/detail`).flush(OVERVIEW);
    flushOptions();
    call('GET', `customers/${ID}/activity`).flush({
      items: [
        {
          id: 'a-1',
          action: 'property.created',
          subjectName: 'Lake house',
          actorName: null,
          occurredAt: '2026-09-20T15:00:00Z',
        },
        {
          id: 'a-2',
          action: 'customer.updated',
          occurredAt: '2026-09-19T15:00:00Z',
          actorName: 'Alex Morgan',
        },
      ],
      totalCount: 25,
      page: 1,
      pageSize: 20,
      timezone: 'America/Chicago',
    });
    await settle();
    const selected = () =>
      host.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim();
    expect(selected()).toBe('Activity');
    expect(text()).toContain('Property added: Lake house');
    expect(text()).toContain('System · Sep 20, 2026');
    expect(text()).toContain('Customer details updated');
    expect(text()).toContain('Page 1 of 2');

    button('Quotes')!.click();
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/customers/${ID}?tab=quotes`);
    expect(selected()).toBe('Quotes');
    expect(text()).toContain('Quotes are coming soon');
    expect(text()).toContain("You'll see this customer's quotes here.");
    expect(button('Go to Quotes')!.getAttribute('href')).toBe('/coming-soon/quotes');

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
});
