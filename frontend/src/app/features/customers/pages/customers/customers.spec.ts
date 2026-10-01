import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { Confirmation, ConfirmationService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { CustomerDrawer } from '../../components/customer-drawer/customer-drawer';
import { ImportDialog } from '../../components/import-dialog/import-dialog';
import { CustomerDetail, CustomerRow } from '../../models/customer.model';
import { Customers } from './customers';

const API = 'http://api.test';
const NO_CONTENT = { status: 204, statusText: 'No Content' };
const SERVER_ERROR = { status: 500, statusText: 'Server Error' };

@Component({ template: '<p>stub</p>' })
class Stub {}

const SOFIA: CustomerRow = {
  id: 'c-1',
  type: 'residential',
  displayName: 'Sofia Martinez',
  primaryEmail: 'sofia@example.com',
  primaryPhone: '5125557832',
  propertyCount: 1,
  lastService: {
    completedAt: '2026-09-20T03:30:00Z',
    summary: 'Kitchen sink leak repair\nSecond line',
  },
  nextService: { startsAt: '2026-09-28T14:00:00Z' },
  balance: 0,
  lifecycle: 'active',
  displayStatus: 'active',
  branchId: 'b-1',
};
const BRIGHTLINE: CustomerRow = {
  ...SOFIA,
  id: 'c-2',
  type: 'commercial',
  displayName: 'BrightLine Offices',
  primaryEmail: 'facilities@brightline.example.com',
  primaryPhone: null,
  propertyCount: 4,
  lastService: null,
  nextService: null,
  balance: 780.5,
  lifecycle: 'active',
  displayStatus: 'overdue',
};
const ARCHIVED: CustomerRow = {
  ...SOFIA,
  id: 'c-3',
  displayName: 'Old Customer',
  lifecycle: 'archived',
  displayStatus: 'archived',
};
const ROWS = [SOFIA, BRIGHTLINE];
const COUNTS = { all: 1248, leads: 86, active: 986, archived: 276 };
const DETAIL: CustomerDetail = {
  id: 'c-1',
  type: 'residential',
  contact: {
    firstName: 'Sofia',
    lastName: 'Martinez',
    title: null,
    email: 'sofia@example.com',
    phone: '5125557832',
    prefersEmail: true,
    prefersSms: false,
  },
  property: { addressLine1: '12 Oak St', city: 'Austin', stateRegion: 'TX', postalCode: '78701' },
  serviceInstructions: null,
  internalNote: null,
  branchId: 'b-1',
  tags: [{ id: 't-1', name: 'VIP' }],
  lifecycle: 'active',
  displayStatus: 'active',
  isActive: true,
};
const METRICS = {
  totalCustomers: 1248,
  activeCustomers: 986,
  newThisMonth: 47,
  outstandingBalance: 18760,
  currency: 'USD',
};
const BRANCHES = {
  countryCode: 'US',
  branches: [
    { id: 'b-1', name: 'Austin Central' },
    { id: 'b-2', name: 'Dallas' },
  ],
};
const TAGS = [
  { id: 't-1', name: 'VIP' },
  { id: 't-2', name: 'Wholesale' },
];
const listBody = (items: readonly CustomerRow[], total = items.length) => ({
  items,
  totalCount: total,
  page: 1,
  pageSize: 10,
  tabCounts: COUNTS,
  currency: 'USD',
  timezone: 'America/Chicago',
});

describe('Customers page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<Customers>;
  let page: Customers;
  let host: HTMLElement;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const list = (): TestRequest => call('GET', 'customers');
  const flushInit = (items: readonly CustomerRow[] = ROWS, total = 25): void => {
    list().flush(listBody(items, total));
    call('GET', 'customers/metrics').flush(METRICS);
    call('GET', 'customers/branch-options').flush(BRANCHES);
    call('GET', 'customer-tags').flush(TAGS);
  };
  const flushRefresh = (): void => {
    list().flush(listBody(ROWS, 25));
    call('GET', 'customers/metrics').flush(METRICS);
  };

  async function setup(roleCode: string, initial: 'ok' | 'none' = 'ok') {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'auth/sign-in', component: Stub }]),
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
    fixture = TestBed.createComponent(Customers);
    page = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    if (initial === 'ok') {
      flushInit();
    }
    await settle();
  }

  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (root: ParentNode = host): string =>
    root.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string, root: ParentNode = host): HTMLButtonElement | undefined =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const drawer = (): CustomerDrawer =>
    fixture.debugElement.query(By.directive(CustomerDrawer)).componentInstance;
  const importDialog = (): ImportDialog =>
    fixture.debugElement.query(By.directive(ImportDialog)).componentInstance;
  const confirmations = (): Confirmation[] => {
    const captured: Confirmation[] = [];
    vi.spyOn(fixture.debugElement.injector.get(ConfirmationService), 'confirm').mockImplementation(
      (confirmation) => {
        captured.push(confirmation);
        return undefined as never;
      },
    );
    return captured;
  };
  const latestList = (): TestRequest => {
    const requests = httpTesting.match((r) => r.method === 'GET' && r.url === `${API}/customers`);
    expect(requests.slice(0, -1).every((r) => r.cancelled)).toBe(true);
    return requests[requests.length - 1];
  };
  const chooseFile = async (name: string): Promise<void> => {
    const input = document.body.querySelector<HTMLInputElement>('#import-file')!;
    const file = new File(['type,email\n'], name, { type: 'text/csv' });
    Object.defineProperty(input, 'files', {
      value: { item: () => file, length: 1 },
      configurable: true,
    });
    input.dispatchEvent(new Event('change'));
    await settle();
  };

  afterEach(() => httpTesting.verify());

  it('renders the list per BR-09 and syncs tabs, search, filters, sort, paging and the empty/error states (AC-01, AC-04 to AC-07, AC-24)', async () => {
    await setup('owner');

    expect(host.querySelector('h1')?.textContent?.trim()).toBe('Customers');
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['1,248', '986', '47', '$18,760']);
    expect(Array.from(host.querySelectorAll('[role="tab"]'), (n) => n.textContent?.trim())).toEqual(
      ['All customers (1,248)', 'Leads (86)', 'Active (986)', 'Archived (276)'],
    );
    // Dates are UTC instants formatted in the organization timezone (America/Chicago).
    const tds = (index: number): HTMLTableCellElement[] =>
      Array.from(
        host.querySelectorAll('app-customer-table tbody tr')[index].querySelectorAll('td'),
      );
    const lines = (td: HTMLElement): (string | undefined)[] =>
      Array.from(td.querySelectorAll('span.customer-table__line'), (n) => n.textContent?.trim());
    const [name, type, contact, properties, last, next, balance, status] = tds(0);
    expect(text(name)).toBe('Sofia Martinez');
    expect(text(type)).toBe('Residential');
    expect(lines(contact)).toEqual(['sofia@example.com', '(512) 555-7832']);
    expect(text(properties)).toBe('1 property');
    expect(lines(last)).toEqual(['Sep 19, 2026', 'Kitchen sink leak repair']);
    expect(lines(next)).toEqual(['Sep 28, 2026', '9:00 AM']);
    expect(text(balance)).toBe('$0');
    expect(text(status)).toContain('Active');
    const second = tds(1);
    expect(text(second[1])).toBe('Commercial');
    expect(lines(second[2])).toEqual(['facilities@brightline.example.com']);
    expect(text(second[3])).toBe('4 properties');
    expect(text(second[4])).toBe('No completed service');
    expect(text(second[5])).toBe('None');
    expect(text(second[6])).toBe('$780.50');
    expect(text(second[7])).toContain('Overdue');
    expect(text()).toContain('Showing 1 – 10 of 25 customers');
    expect(
      host.querySelector('button[aria-label="Actions for BrightLine Offices"]'),
    ).not.toBeNull();

    // Paging, then every tab / filter / sort change returns to page 1 and cancels stale requests.
    host.querySelector<HTMLButtonElement>('button[aria-label="Page 2"]')!.click();
    let request = list();
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('tab')).toBe('all');
    expect(request.request.params.get('sort')).toBe('last_activity');
    expect(request.request.params.has('search')).toBe(false);
    request.flush({ ...listBody(ROWS, 25), page: 2 });
    await settle();
    expect(text()).toContain('Showing 11 – 20 of 25 customers');

    host.querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    request = list();
    expect(request.request.params.get('tab')).toBe('leads');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(listBody(ROWS, 25));
    await settle();
    expect(host.querySelectorAll('[role="tab"]')[1].getAttribute('aria-selected')).toBe('true');

    page.onType('commercial');
    page.onBranch('b-1');
    page.onTags(['t-1', 't-2']);
    page.onBalance('overdue');
    page.onSort('balance_desc');
    // The page-local Branch filter also scopes the metrics.
    const scoped = call('GET', 'customers/metrics');
    expect(scoped.request.params.get('branchId')).toBe('b-1');
    scoped.flush(METRICS);
    request = latestList();
    const params = request.request.params;
    expect(params.get('type')).toBe('commercial');
    expect(params.get('branchId')).toBe('b-1');
    expect(params.getAll('tagIds')).toEqual(['t-1', 't-2']);
    expect(params.get('balanceStatus')).toBe('overdue');
    expect(params.get('sort')).toBe('balance_desc');
    expect(params.get('page')).toBe('1');
    request.flush(listBody(ROWS, 25));
    await settle();

    // The Name header toggles Name A-Z / Z-A and exposes the sort state.
    host.querySelector<HTMLButtonElement>('.customer-table__sort')!.click();
    list().flush(listBody(ROWS, 25));
    await settle();
    expect(host.querySelector('th[aria-sort]')?.getAttribute('aria-sort')).toBe('ascending');
    host.querySelector<HTMLButtonElement>('.customer-table__sort')!.click();
    request = list();
    expect(request.request.params.get('sort')).toBe('name_desc');
    request.flush(listBody(ROWS, 25));
    await settle();
    expect(host.querySelector('th[aria-sort]')?.getAttribute('aria-sort')).toBe('descending');

    // Search is debounced by 300 ms.
    const search = host.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'kim';
    search.dispatchEvent(new Event('input'));
    httpTesting.expectNone((r) => r.url === `${API}/customers`);
    await new Promise((resolve) => setTimeout(resolve, 350));
    request = list();
    expect(request.request.params.get('search')).toBe('kim');
    request.flush(listBody([], 0));
    await settle();

    // Filtered-empty offers Clear filters (tab and sort stay); an unfiltered empty list is the initial empty.
    expect(text()).toContain('No customers match your filters.');
    button('Clear filters')!.click();
    const metrics = call('GET', 'customers/metrics');
    expect(metrics.request.params.has('branchId')).toBe(false);
    metrics.flush(METRICS);
    request = list();
    for (const key of ['search', 'type', 'branchId', 'tagIds', 'balanceStatus']) {
      expect(request.request.params.has(key)).toBe(false);
    }
    expect(request.request.params.get('tab')).toBe('leads');
    request.flush(listBody([], 0));
    await settle();
    page.onTab('all');
    list().flush(listBody([], 0));
    await settle();
    expect(text()).toContain('No customers yet');
    expect(text()).toContain('Add your first customer or import a CSV.');
    expect(button('New customer')).toBeDefined();
    page.onTab('archived');
    list().flush(listBody([], 0));
    await settle();
    expect(text()).toContain('No archived customers.');

    // Error replaces the table (per region) and Retry reloads it; failed metrics keep the table.
    page.loadList();
    list().flush(null, SERVER_ERROR);
    await settle();
    expect(text()).toContain("We couldn't load customers.");
    button('Retry')!.click();
    list().flush(listBody(ROWS, 2));
    await settle();
    expect(host.querySelector('app-customer-table')).not.toBeNull();
    page.loadMetrics();
    call('GET', 'customers/metrics').flush(null, SERVER_ERROR);
    await settle();
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['—', '—', '—', '—']);
    expect(host.querySelector('app-customer-table')).not.toBeNull();
  }, 20_000);

  it.each([
    ['owner', { create: true, import: true, menu: ['Edit', 'Archive'] }],
    ['dispatcher', { create: true, import: false, menu: ['Edit', 'Archive'] }],
    ['operations_manager', { create: false, import: false, menu: ['View'] }],
    ['accounting', { create: false, import: false, menu: ['View'] }],
    ['viewer', { create: false, import: false, menu: ['View'] }],
    ['technician', { create: false, import: false, menu: null }],
  ] as const)(
    'adapts the UI to the %s role; read roles open a read-only drawer; technicians get the forbidden state without requests (BR-18, AC-21)',
    async (role, expected) => {
      if (expected.menu === null) {
        await setup(role, 'none');
        // `verify()` in afterEach fails on any pending request.
        expect(text()).toContain("You don't have access to customers.");
        expect(host.querySelector('app-customer-table')).toBeNull();
        expect(button('New customer')).toBeUndefined();
        expect(button('Import customers')).toBeUndefined();
        return;
      }
      await setup(role);

      // Branch filter/drawer options and country come only from GET /customers/branch-options.
      expect(page.branches().map((b) => b.id)).toEqual(['b-1', 'b-2']);
      expect(page.countryCode()).toBe('US');
      expect(button('New customer') !== undefined).toBe(expected.create);
      expect(button('Import customers') !== undefined).toBe(expected.import);
      expect(page.buildMenu(SOFIA).map((item) => item.label)).toEqual(expected.menu);
      expect(page.buildMenu(ARCHIVED).map((item) => item.label)).toEqual(
        expected.create ? ['Edit', 'Reactivate'] : ['View'],
      );

      if (!expected.create) {
        host.querySelector<HTMLButtonElement>('.customer-table__link')!.click();
        await settle();
        call('GET', 'customers/c-1').flush(DETAIL);
        await settle();
        expect(host.querySelector('.customer-drawer__title')?.textContent?.trim()).toBe(
          'Customer details',
        );
        expect(button('Close')).toBeDefined();
        expect(button('Create customer')).toBeUndefined();
        expect(button('Save changes')).toBeUndefined();
        expect(host.querySelector<HTMLInputElement>('#customer-email')?.disabled).toBe(true);
        // No duplicate check or tag creation in read-only mode (`verify()` fails on any request).
        drawer().patch({ email: 'other@example.com' }, 'email');
        drawer().onFieldBlur('email');
        drawer().onTagCreate('X');
        await new Promise((resolve) => setTimeout(resolve, 550));
      }
    },
  );

  it('creates a customer: field messages, SMS rule, duplicate gating with Create anyway, tag creation and server errors (AC-09, AC-10, AC-12, AC-17, AC-24)', async () => {
    await setup('owner');
    button('New customer')!.click();
    await settle();
    const d = drawer();
    expect(host.querySelector('.customer-drawer__title')?.textContent?.trim()).toBe('New customer');
    expect(d.form().prefersEmail).toBe(true);
    expect(d.form().prefersSms).toBe(false);

    // Commercial adds Company name and Title.
    d.onType('commercial');
    await settle();
    expect(host.querySelector('#customer-company-name')).not.toBeNull();
    expect(host.querySelector('#customer-title')).not.toBeNull();
    d.onType('residential');

    // Submitting an empty form shows the messages and does not call the API.
    button('Create customer')!.click();
    await settle();
    const errors = Array.from(host.querySelectorAll('.form-field__error'), (n) => text(n));
    for (const message of [
      'Enter a first name.',
      'Enter a last name.',
      'Enter an email.',
      'Enter an address.',
      'Enter a city.',
      'Choose a branch.',
    ]) {
      expect(errors).toContain(message);
    }
    expect(host.querySelector('#customer-first-name')?.getAttribute('aria-invalid')).toBe('true');
    d.patch({ prefersSms: true }, 'preferences');
    await settle();
    expect(text()).toContain('Add a mobile phone to use SMS.');

    d.patch({
      firstName: 'Sofia',
      lastName: 'Martinez',
      email: ' Sofia.Martinez@Example.com ',
      phone: '(512) 555-7832',
      prefersSms: false,
      addressLine1: '12 Oak St',
      city: 'Austin',
      stateRegion: 'TX',
      postalCode: '78701',
      branchId: 'b-1',
    });

    // A match gates saving until Create anyway; the server is never asked to reject it.
    d.onFieldBlur('email');
    const check = call('POST', 'customers/duplicate-check');
    expect(check.request.body).toEqual({
      email: 'sofia.martinez@example.com',
      phone: '5125557832',
    });
    check.flush({
      matches: [
        {
          customerId: 'c-9',
          displayName: 'Sofia Martinez',
          primaryEmail: 'sofia.martinez@example.com',
          primaryPhone: '5125557832',
          propertyCount: 1,
          displayStatus: 'archived',
          matchedField: 'email',
          inScope: true,
        },
      ],
    });
    await settle();
    expect(text()).toContain('Possible existing customer');
    expect(text()).toContain('1 property · Archived');
    expect(text()).toContain('Exact email or phone matches require review.');
    expect(text()).toContain('Resolve the possible match to continue.');
    expect(button('Create customer')!.disabled).toBe(true);
    d.submit();
    httpTesting.expectNone((r) => r.method === 'POST' && r.url === `${API}/customers`);
    button('Create anyway')!.click();
    await settle();
    expect(text()).not.toContain('Possible existing customer');
    expect(button('Create customer')!.disabled).toBe(false);

    // Inline tag creation persists the tag immediately and selects it; the 11th tag is refused.
    d.onTagCreate('Priority');
    const tag = call('POST', 'customer-tags');
    expect(tag.request.body).toEqual({ name: 'Priority' });
    tag.flush({ id: 't-3', name: 'Priority' }, { status: 201, statusText: 'Created' });
    await settle();
    expect(d.form().tagIds).toEqual(['t-3']);
    expect(page.tags().map((t) => t.name)).toEqual(['Priority', 'VIP', 'Wholesale']);
    d.patch({ tagIds: Array.from({ length: 10 }, (_, i) => `t-${i}`) });
    d.onTagCreate('Eleventh');
    httpTesting.expectNone((r) => r.url === `${API}/customer-tags`);
    d.patch({ tagIds: ['t-3'] });

    // A 400 is mapped under its field and keeps the drawer open and the data; then it saves.
    button('Create customer')!.click();
    let save = call('POST', 'customers');
    expect(save.request.body).toEqual({
      type: 'residential',
      companyName: null,
      contact: {
        firstName: 'Sofia',
        lastName: 'Martinez',
        title: null,
        email: 'sofia.martinez@example.com',
        phone: '5125557832',
        prefersEmail: true,
        prefersSms: false,
      },
      property: {
        addressLine1: '12 Oak St',
        city: 'Austin',
        stateRegion: 'TX',
        postalCode: '78701',
      },
      serviceInstructions: null,
      internalNote: null,
      branchId: 'b-1',
      tagIds: ['t-3'],
    });
    save.flush(
      { errors: { 'contact.email': ['Enter a valid email.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Enter a valid email.');
    expect(drawer().open()).toBe(true);
    expect(d.form().firstName).toBe('Sofia');

    button('Create customer')!.click();
    save = call('POST', 'customers');
    save.flush({ id: 'c-10' }, { status: 201, statusText: 'Created' });
    await settle();
    expect(page.drawerOpen()).toBe(false);
    flushRefresh();
    await settle();
  }, 20_000);

  it('opens the existing customer after a discard confirmation, clears the matched field and never blocks on a failed check; archive asks first and maps 409 (AC-13, BR-15, BR-16)', async () => {
    await setup('dispatcher');
    button('New customer')!.click();
    await settle();
    const d = drawer();
    const dialogs = confirmations();
    d.patch({ firstName: 'Dirty', email: 'sofia@example.com', phone: '5125557832' });
    d.onFieldBlur('phone');
    call('POST', 'customers/duplicate-check').flush({
      matches: [
        {
          customerId: 'c-1',
          displayName: 'Sofia Martinez',
          primaryEmail: 'sofia@example.com',
          primaryPhone: '5125557832',
          propertyCount: 2,
          displayStatus: 'active',
          matchedField: 'phone',
          inScope: true,
        },
        {
          displayName: 'Hidden Customer',
          displayStatus: 'lead',
          matchedField: 'email',
          inScope: false,
        },
      ],
    });
    await settle();
    expect(text()).toContain('2 properties · Active');
    expect(text()).toContain('Hidden Customer');
    expect(button('Use different phone')).toBeDefined();
    expect(button('Use different email')).toBeDefined();

    // Out-of-scope matches never get a View action; the dirty form asks before leaving.
    button('View existing customer')!.click();
    expect(dialogs[0].header).toBe('Discard unsaved changes?');
    expect(dialogs[0].acceptButtonProps?.label).toBe('Discard');
    expect(dialogs[0].rejectButtonProps?.label).toBe('Keep editing');
    dialogs[0].accept!();
    await settle();
    expect(page.drawerMode()).toBe('edit');
    call('GET', 'customers/c-1').flush(DETAIL);
    await settle();
    expect(host.querySelector('.customer-drawer__title')?.textContent?.trim()).toBe(
      'Edit customer',
    );
    expect(host.querySelector<HTMLInputElement>('#customer-first-name')?.value).toBe('Sofia');
    expect(host.querySelector<HTMLInputElement>('#customer-phone')?.value).toBe('(512) 555-7832');
    expect(host.querySelector<HTMLFieldSetElement>('.customer-drawer__type')?.disabled).toBe(false);

    // Edit excludes itself from the check; a failed check shows no panel and does not block.
    d.patch({ email: 'other@example.com' }, 'email');
    d.onFieldBlur('email');
    const check = call('POST', 'customers/duplicate-check');
    expect(check.request.body.excludeCustomerId).toBe('c-1');
    check.flush(null, SERVER_ERROR);
    await settle();
    expect(text()).not.toContain('Possible existing customer');
    expect(button('Save changes')!.disabled).toBe(false);

    // Use different phone clears and focuses the field.
    d.patch({ phone: '5125550000' });
    d.onFieldBlur('phone');
    call('POST', 'customers/duplicate-check').flush({
      matches: [
        {
          customerId: 'c-1',
          displayName: 'Sofia Martinez',
          propertyCount: 1,
          displayStatus: 'active',
          matchedField: 'phone',
          inScope: true,
        },
      ],
    });
    await settle();
    button('Use different phone')!.click();
    await settle();
    expect(d.form().phone).toBe('');
    expect(document.activeElement?.id).toBe('customer-phone');
    // Clearing the phone re-runs the check for the remaining email after 500 ms of idle.
    await new Promise((resolve) => setTimeout(resolve, 550));
    call('POST', 'customers/duplicate-check').flush({ matches: [] });

    // Archive: confirmation dialog, then 409 shows its message and refreshes.
    page.onDrawerClosed();
    page.buildMenu(SOFIA)[1].command!({});
    expect(dialogs[1].header).toBe('Archive Sofia Martinez?');
    expect(dialogs[1].message).toBe(
      'Archived customers are hidden from active lists. Their contacts, properties and history are kept.',
    );
    dialogs[1].accept!();
    call('POST', 'customers/c-1/archive').flush(
      { errors: {} },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    flushRefresh();
    await settle();
    page.buildMenu(ARCHIVED)[1].command!({});
    call('POST', 'customers/c-3/reactivate').flush(null, NO_CONTENT);
    await settle();
    flushRefresh();
  }, 20_000);

  it('keeps the paginator with a single page and confirms an edit-mode type change before it is saved (BR-09, BR-21, AC-07, AC-15)', async () => {
    await setup('owner');
    host.querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    list().flush(listBody(ROWS, 2));
    await settle();
    const nav = host.querySelector('nav[aria-label="Pagination"]')!;
    expect(nav.querySelector('button[aria-label="Page 1"]')?.getAttribute('aria-current')).toBe(
      'page',
    );
    expect(
      nav.querySelector<HTMLButtonElement>('button[aria-label="Previous page"]')!.disabled,
    ).toBe(true);
    expect(nav.querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.disabled).toBe(
      true,
    );

    page.onNameClicked(SOFIA);
    await settle();
    const dialogs = confirmations();
    call('GET', 'customers/c-1').flush(DETAIL);
    await settle();
    const d = drawer();
    const typeFieldset = host.querySelector<HTMLFieldSetElement>('.customer-drawer__type')!;
    expect(typeFieldset.disabled).toBe(false);

    // Cancel keeps the type and the form untouched.
    d.onType('commercial');
    expect(dialogs[0].header).toBe('Change to a commercial customer?');
    expect(dialogs[0].message).toBe(
      'Add a company name. Sofia Martinez stays the primary contact.',
    );
    expect(dialogs[0].acceptButtonProps?.label).toBe('Change type');
    expect(dialogs[0].rejectButtonProps?.label).toBe('Cancel');
    dialogs[0].reject!();
    await settle();
    expect(d.form().type).toBe('residential');
    expect(d.dirty()).toBe(false);

    // Confirm switches the form and makes it dirty; the PUT carries the type.
    d.onType('commercial');
    dialogs[1].accept!();
    await settle();
    expect(d.dirty()).toBe(true);
    expect(host.querySelector<HTMLInputElement>('#customer-company-name')?.value).toBe('');
    expect(host.querySelector('#customer-title')).not.toBeNull();
    button('Save changes')!.click();
    await settle();
    expect(text()).toContain('Enter a company name.');
    d.patch({ companyName: 'Sofia Plumbing', title: 'Owner' }, 'companyName');
    button('Save changes')!.click();
    let save = call('PUT', 'customers/c-1');
    expect(save.request.body.type).toBe('commercial');
    expect(save.request.body.companyName).toBe('Sofia Plumbing');
    expect(save.request.body.contact.title).toBe('Owner');
    save.flush({ errors: { type: ['Server text.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();
    expect(d.error('type')).toBe('Choose a customer type.');

    // Commercial to residential warns, then drops company name and title.
    d.onType('residential');
    expect(dialogs[2].header).toBe('Change to a residential customer?');
    expect(dialogs[2].message).toBe(
      "Sofia Plumbing will no longer be the display name. The customer will be shown as the primary contact's name, and the company name and contact title will be removed when you save.",
    );
    dialogs[2].accept!();
    await settle();
    expect(host.querySelector('#customer-company-name')).toBeNull();
    expect(host.querySelector('#customer-title')).toBeNull();
    button('Save changes')!.click();
    save = call('PUT', 'customers/c-1');
    expect(save.request.body.type).toBe('residential');
    expect(save.request.body.companyName).toBeNull();
    expect(save.request.body.contact.title).toBeNull();
    expect(save.request.body.contact.firstName).toBe('Sofia');
    save.flush({ ...DETAIL });
    await settle();
    flushRefresh();
  }, 20_000);

  it('previews a CSV, blocks on row errors, shows duplicate warnings without blocking and confirms all-or-nothing (AC-22, AC-23)', async () => {
    await setup('owner');
    button('Import customers')!.click();
    await settle();
    expect(importDialog().visible()).toBe(true);
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(button('Import customers', dialog)?.disabled).toBe(true);

    // Client-side type check, then a preview with row errors keeps Import disabled.
    await chooseFile('notes.txt');
    expect(text(dialog)).toContain('Choose a CSV file.');
    await chooseFile('customers.csv');
    const preview = call('POST', 'customers/import/preview');
    expect(preview.request.body instanceof FormData).toBe(true);
    preview.flush({
      validRowCount: 0,
      rowErrors: [{ row: 3, column: 'email', message: 'Enter a valid email.' }],
      duplicateWarnings: [],
    });
    await settle();
    expect(text(dialog)).toContain('Enter a valid email.');
    expect(importDialog().canImport()).toBe(false);

    // A clean preview lists warnings but never blocks; confirm re-uploads and refreshes.
    await chooseFile('fixed.csv');
    call('POST', 'customers/import/preview').flush({
      validRowCount: 12,
      rowErrors: [],
      duplicateWarnings: [{ row: 4, matchedField: 'email', existingDisplayName: 'Sofia Martinez' }],
    });
    await settle();
    expect(text(dialog)).toContain('12 customers ready to import');
    expect(text(dialog)).toContain('Row 4');
    expect(importDialog().canImport()).toBe(true);
    importDialog().submit();
    call('POST', 'customers/import').flush(
      { rowErrors: [{ row: 9, column: 'branch_code', message: 'Unknown branch code.' }] },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text(dialog)).toContain('Unknown branch code.');
    expect(importDialog().canImport()).toBe(false);

    await chooseFile('fixed-again.csv');
    call('POST', 'customers/import/preview').flush({
      validRowCount: 12,
      rowErrors: [],
      duplicateWarnings: [],
    });
    await settle();
    importDialog().submit();
    call('POST', 'customers/import').flush({ importedCount: 12 });
    await settle();
    expect(importDialog().visible()).toBe(false);
    call('GET', 'customer-tags').flush(TAGS);
    flushRefresh();
  }, 20_000);
});
