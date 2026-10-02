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
import { Confirmation, ConfirmationService, MenuItem, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { ImportDialog } from '../../components/import-dialog/import-dialog';
import { ItemDrawer } from '../../components/item-drawer/item-drawer';
import { CatalogCategory, CatalogDetail, CatalogRow } from '../../models/catalog.model';
import { ProductsServices } from './products-services';

const API = 'http://api.test';
const NO_CONTENT = { status: 204, statusText: 'No Content' };

@Component({ template: '<p>stub</p>' })
class Stub {}

const row = (over: Partial<CatalogRow>): CatalogRow => ({
  id: 'c-1',
  type: 'service',
  name: 'Kitchen sink leak repair',
  description: 'Diagnose and repair a leaking kitchen sink',
  unitCost: 90,
  unitPrice: 240,
  estimatedMarginPercent: 62.5,
  isTaxable: true,
  isActive: true,
  hasImage: false,
  updatedAt: '2026-09-30T10:00:00Z',
  ...over,
});
const SINK = row({});
const VALVE = row({
  id: 'c-2',
  type: 'product',
  name: 'Brass shut-off valve',
  description: null,
  unitCost: 18.5,
  unitPrice: 0,
  estimatedMarginPercent: null,
  isTaxable: false,
  isActive: false,
});
const ROWS = [SINK, VALVE];
const detail = (over: Partial<CatalogDetail> = {}): CatalogDetail => ({
  ...SINK,
  categoryId: null,
  categoryName: null,
  categoryIsActive: null,
  image: null,
  usage: { quotes: 1, jobs: 14, invoices: 0 },
  ...over,
});
const category = (over: Partial<CatalogCategory>): CatalogCategory => ({
  id: 'k-1',
  name: 'Plumbing',
  isActive: true,
  itemCount: 3,
  activeServiceCount: 2,
  ...over,
});
const PLUMBING = category({});
const ELECTRICAL = category({ id: 'k-2', name: 'Electrical', isActive: false, itemCount: 1 });
const CATEGORIES = [ELECTRICAL, PLUMBING];
const INACTIVE_ITEM = detail({
  categoryId: 'k-2',
  categoryName: 'Electrical',
  categoryIsActive: false,
});
const PROBLEM = { status: 400, statusText: 'Bad Request' };
const SUMMARY = {
  publicRequestReadiness: 'ready',
  activeItems: 41,
  activeServices: 24,
  activeProducts: 17,
  inactiveItems: 3,
  allItems: 44,
  services: 26,
  products: 18,
  currency: 'USD',
};

describe('Products & services page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<ProductsServices>;
  let page: ProductsServices;
  let host: HTMLElement;

  const list = (): TestRequest => httpTesting.expectOne((r) => r.url === `${API}/catalog-items`);
  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const flushList = (items: readonly CatalogRow[] = ROWS, total = items.length): void =>
    list().flush({ items, page: 1, pageSize: 10, totalCount: total });
  const refresh = (): void => {
    flushList();
    call('GET', 'catalog-items/summary').flush(SUMMARY);
  };

  async function setup(roleCode: string, initial: 'ok' | 'none' = 'ok', summary = SUMMARY) {
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

    fixture = TestBed.createComponent(ProductsServices);
    page = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    if (initial === 'ok') {
      flushList();
      call('GET', 'catalog-items/summary').flush(summary);
      if (roleCode === 'owner' || roleCode === 'operations_manager') {
        call('GET', 'catalog-categories').flush({ items: CATEGORIES });
      }
    }
    await settle();
  }

  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (root: ParentNode = host): string => root.textContent?.replace(/\s+/g, ' ') ?? '';
  const button = (label: string, root: ParentNode = host): HTMLButtonElement | undefined =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const drawer = (): ItemDrawer =>
    fixture.debugElement.query(By.directive(ItemDrawer)).componentInstance;
  const importDialog = (): ImportDialog =>
    fixture.debugElement.query(By.directive(ImportDialog)).componentInstance;
  const inject = <T>(token: new (...args: never[]) => T): T =>
    fixture.debugElement.injector.get(token);
  const pick = async (selector: string, file: File, root: ParentNode = host): Promise<void> => {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    Object.defineProperty(input, 'files', {
      value: { item: () => file, length: 1 },
      configurable: true,
    });
    input.dispatchEvent(new Event('change'));
    await settle();
  };
  const typeInto = async (selector: string, value: string): Promise<void> => {
    const input = document.body.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  };
  const key = async (selector: string, name: string): Promise<void> => {
    document.body
      .querySelector(selector)!
      .dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true }));
    await settle();
  };
  const file = (name: string, type: string, size = 10): File =>
    new File([new Uint8Array(size)], name, { type });
  const confirmations = (): Confirmation[] => {
    const captured: Confirmation[] = [];
    vi.spyOn(inject(ConfirmationService), 'confirm').mockImplementation((confirmation) => {
      captured.push(confirmation);
      return undefined as never;
    });
    return captured;
  };

  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
  });
  afterEach(() => httpTesting.verify());

  it('renders the catalog and drives tabs, filters, search, sort, paging and the empty/error/partial states (FR-01, FR-03, FR-04, AC-01, AC-04)', async () => {
    await setup('owner');

    expect(host.querySelector('h1')?.textContent?.trim()).toBe('Products & services');
    expect(
      host.querySelector('nav[aria-label="Administration"] [aria-current="page"]')?.textContent,
    ).toContain('Products & services');
    expect(text()).toContain('Manage reusable line items for quotes, jobs, and invoices.');
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['41', '24', '17', '3']);
    expect(Array.from(host.querySelectorAll('[role="tab"]'), (n) => n.textContent?.trim())).toEqual(
      ['All items (44)', 'Services (26)', 'Products (18)'],
    );
    expect(
      Array.from(host.querySelectorAll('app-catalog-table thead th'), (n) => n.textContent?.trim()),
    ).toEqual([
      'Name',
      'Type',
      'Description',
      'Unit cost',
      'Unit price',
      'Estimated margin',
      'Taxable',
      'Status',
      'Actions',
    ]);
    const cells = Array.from(host.querySelectorAll('app-catalog-table tbody tr')).map((tr) =>
      text(tr),
    );
    expect(cells[0]).toContain('$90.00');
    expect(cells[0]).toContain('$240.00');
    expect(cells[0]).toContain('62.5%');
    expect(cells[0]).toContain('Active');
    expect(cells[1]).toContain('—');
    expect(cells[1]).toContain('Inactive');
    expect(text()).toContain('Showing 1 – 2 of 2 items');
    expect(text()).toContain('Catalog items are defaults.');
    expect(text()).toContain('Discounts are added as adjustments');
    expect(button('Import CSV')).toBeDefined();
    expect(button('Download CSV')).toBeDefined();
    expect(
      host.querySelector('button[aria-label="More actions for Brass shut-off valve"]'),
    ).not.toBeNull();

    // Tabs and Type are one state; every change resets to page 1.
    page.onPageChange({ page: 2 });
    list().flush({ items: ROWS, page: 3, pageSize: 10, totalCount: 24 });
    host.querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    let request = list();
    expect(request.request.params.get('type')).toBe('service');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ items: [SINK], page: 1, pageSize: 10, totalCount: 24 });
    await settle();
    expect(host.querySelectorAll('[role="tab"]')[1].getAttribute('aria-selected')).toBe('true');
    expect(text()).toContain('Showing 1 – 10 of 24 items');

    page.onType('product');
    await settle();
    expect(host.querySelectorAll('[role="tab"]')[2].getAttribute('aria-selected')).toBe('true');
    page.onTaxStatus('non_taxable');
    page.onStatus('inactive');
    page.onSort('unitPrice');
    page.onSort('unitPrice');
    const requests = httpTesting.match((r) => r.url === `${API}/catalog-items`);
    const latest = requests[requests.length - 1].request.params;
    expect(latest.get('type')).toBe('product');
    expect(latest.get('taxStatus')).toBe('non_taxable');
    expect(latest.get('status')).toBe('inactive');
    expect(latest.get('sort')).toBe('-unitPrice');
    expect(requests.slice(0, -1).every((r) => r.cancelled)).toBe(true);
    requests[requests.length - 1].flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    await settle();
    expect(text()).toContain('No items match these filters.');

    // Search is debounced by 300 ms.
    const search = host.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'sink';
    search.dispatchEvent(new Event('input'));
    httpTesting.expectNone((r) => r.url === `${API}/catalog-items`);
    await new Promise((resolve) => setTimeout(resolve, 350));
    request = list();
    expect(request.request.params.get('search')).toBe('sink');
    request.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    await settle();

    // Clear filters resets tab, search and filters; an unfiltered empty list is the initial empty.
    button('Clear filters')!.click();
    request = list();
    for (const key of ['type', 'search', 'taxStatus', 'status']) {
      expect(request.request.params.has(key)).toBe(false);
    }
    request.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    await settle();
    expect(text()).toContain('No products or services yet.');
    expect(button('Add item')).toBeDefined();

    // List error replaces the table; Retry reloads. Failed metrics show "—" and keep the table.
    page.loadList();
    list().flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load items. Check your connection and try again.");
    button('Retry')!.click();
    flushList();
    await settle();
    page.loadSummary();
    call('GET', 'catalog-items/summary').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['—', '—', '—', '—']);
    expect(host.querySelectorAll('[role="tab"]')[0].textContent).toContain('All items (—)');
    expect(host.querySelector('app-catalog-table')).not.toBeNull();
  }, 15_000);

  it('shows the forbidden state for a technician without any data request (FR-17, AC-20)', async () => {
    await setup('technician', 'none');

    // `verify()` in afterEach fails on any pending request.
    expect(text()).toContain("You don't have access to products and services.");
    expect(host.querySelector('app-catalog-table')).toBeNull();
    expect(host.querySelector('nav[aria-label="Administration"]')).toBeNull();
    expect(button('Add item')).toBeUndefined();
  });

  it.each([
    ['owner', { add: true, import: true, csv: true, admin: true, banner: false, menu: ['Edit'] }],
    [
      'operations_manager',
      { add: true, import: false, csv: false, admin: false, banner: false, menu: ['Edit'] },
    ],
    [
      'viewer',
      { add: false, import: false, csv: false, admin: true, banner: true, menu: ['View details'] },
    ],
    [
      'accounting',
      { add: false, import: false, csv: false, admin: false, banner: true, menu: ['View details'] },
    ],
  ] as const)(
    'adapts the UI to the %s role and opens the read-only details drawer for read roles (FR-17, AC-20)',
    async (role, expected) => {
      await setup(role);

      expect(button('Add item') !== undefined).toBe(expected.add);
      expect(button('Import CSV') !== undefined).toBe(expected.import);
      expect(button('Manage categories') !== undefined).toBe(expected.add);
      expect(text()).toContain('Public service requests are ready.');
      expect(button('Download CSV') !== undefined).toBe(expected.csv);
      expect(button('Export list')).toBeDefined();
      expect(host.querySelector('nav[aria-label="Administration"]') !== null).toBe(expected.admin);
      expect(text().includes('You have view-only access. Adding and editing items')).toBe(
        expected.banner,
      );
      const labels = (r: CatalogRow): (string | undefined)[] =>
        page.buildMenu(r).map((item: MenuItem) => item.label);
      expect(labels(SINK)).toEqual(expected.add ? ['Edit', 'Deactivate'] : ['View details']);
      expect(labels(VALVE)).toEqual(expected.add ? ['Edit', 'Activate'] : ['View details']);

      if (expected.banner) {
        page.buildMenu(SINK)[0].command!({});
        await settle();
        call('GET', 'catalog-items/c-1').flush(
          detail({ image: { contentType: 'image/png', sizeBytes: 5, updatedAt: '' } }),
        );
        call('GET', 'catalog-items/c-1/image').flush(new Blob(['x'], { type: 'image/png' }));
        await settle();
        expect(host.querySelector('.item-drawer__title')?.textContent?.trim()).toBe('Item details');
        expect(button('Close')).toBeDefined();
        expect(button('Save item')).toBeUndefined();
        expect(host.querySelector<HTMLInputElement>('#item-name')?.disabled).toBe(true);
        expect(button('Replace')).toBeUndefined();
        expect(text(host.querySelector('app-item-drawer')!)).toContain('No category');
        expect(host.querySelector('app-item-drawer p-select')).toBeNull();
        expect(host.querySelector('img[alt="Item image preview"]')).not.toBeNull();
        expect(text()).toContain('Used on 1 quote, 14 jobs, and 0 invoices.');
      }
    },
  );

  it('manages categories in the dialog: states, create, inline rename, deactivate confirmation, reactivate and indicator refresh (FR-09, AC-12 to AC-14)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');
    const asked = confirmations();
    const dialog = document.body;
    const summaryRefresh = async (readiness: string): Promise<void> => {
      call('GET', 'catalog-items/summary').flush({ ...SUMMARY, publicRequestReadiness: readiness });
      await settle();
    };
    const rowButton = (label: string): HTMLButtonElement =>
      dialog.querySelector<HTMLButtonElement>(`[aria-label="${label}"]`)!;

    button('Manage categories')!.click();
    await settle();
    expect(text(dialog)).toContain('Manage categories');
    call('GET', 'catalog-categories').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text(dialog)).toContain(
      "We couldn't load categories. Check your connection and try again.",
    );
    expect(dialog.querySelector('.categories__list')).toBeNull();
    button('Retry', dialog)!.click();
    call('GET', 'catalog-categories').flush({ items: [] });
    await settle();
    expect(text(dialog)).toContain('No categories yet. Add one to group your services.');

    // Create: validation, 409 keeps the text under the input, success clears and refreshes.
    button('Add category', dialog)!.click();
    await settle();
    expect(text(dialog)).toContain('Enter a category name.');
    await typeInto('#category-name', ' Plumbing ');
    button('Add category', dialog)!.click();
    await settle();
    let request = call('POST', 'catalog-categories');
    expect(request.request.body).toEqual({ name: 'Plumbing' });
    expect(dialog.querySelector<HTMLInputElement>('#category-name')!.disabled).toBe(true);
    expect(button('Add category', dialog)!.disabled).toBe(true);
    request.flush(
      { status: 409, errors: { name: ['server text'] } },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text(dialog)).toContain('A category with this name already exists.');
    expect(dialog.querySelector<HTMLInputElement>('#category-name')!.value).toBe(' Plumbing ');
    button('Add category', dialog)!.click();
    await settle();
    call('POST', 'catalog-categories').flush(category({ itemCount: 0, activeServiceCount: 0 }), {
      status: 201,
      statusText: 'Created',
    });
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Category added' });
    expect(dialog.querySelector<HTMLInputElement>('#category-name')!.value).toBe('');
    expect(text(dialog)).toContain('0 items · 0 active services');
    await summaryRefresh('no_active_services');
    expect(text()).toContain('Assign an active service to an active category');

    // Inline rename: focus moves in and back, Escape cancels, 409 stays, save returns to view mode.
    rowButton('Rename Plumbing').click();
    await settle();
    expect(document.activeElement?.id).toBe('category-rename-input');
    await key('#category-rename-input', 'Escape');
    expect(dialog.querySelector('#category-rename-input')).toBeNull();
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Rename Plumbing');
    rowButton('Rename Plumbing').click();
    await settle();
    await typeInto('#category-rename-input', 'Drains');
    button('Save', dialog)!.click();
    await settle();
    request = call('PUT', 'catalog-categories/k-1');
    expect(request.request.body).toEqual({ name: 'Drains' });
    request.flush(
      { status: 409, errors: { name: ['x'] } },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text(dialog)).toContain('A category with this name already exists.');
    expect(dialog.querySelector<HTMLInputElement>('#category-rename-input')!.value).toBe('Drains');
    button('Save', dialog)!.click();
    await settle();
    call('PUT', 'catalog-categories/k-1').flush(
      category({ name: 'Drains', itemCount: 1, activeServiceCount: 1 }),
    );
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Category renamed' });
    expect(dialog.querySelector('#category-rename-input')).toBeNull();
    expect(text(dialog)).toContain('1 item · 1 active service');
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Rename Drains');
    await summaryRefresh('ready');

    // Deactivate asks first; a failure keeps the state; reactivate needs no confirmation.
    rowButton('Deactivate Drains').click();
    expect(asked[0]).toMatchObject({
      message:
        'Deactivate Drains? Its services will no longer appear in the public request form. Catalog items will not be deleted.',
      acceptButtonProps: { label: 'Deactivate' },
      rejectButtonProps: { label: 'Cancel' },
    });
    httpTesting.expectNone((r) => r.method === 'POST');
    asked[0].accept!();
    await settle();
    expect(rowButton('Deactivate Drains').disabled).toBe(true);
    call('POST', 'catalog-categories/k-1/deactivate').flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: "We couldn't update Drains. Try again.",
    });
    expect(text(dialog.querySelector('.categories__list')!)).not.toContain('Inactive');
    asked[0].accept!();
    call('POST', 'catalog-categories/k-1/deactivate').flush(null, NO_CONTENT);
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Drains deactivated' });
    expect(text(dialog.querySelector('.categories__list')!)).toContain('Inactive');
    await summaryRefresh('no_active_categories');
    expect(text()).toContain('Create and activate a category to accept public requests.');
    rowButton('Reactivate Drains').click();
    call('POST', 'catalog-categories/k-1/activate').flush(null, NO_CONTENT);
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Drains reactivated' });
    await summaryRefresh('ready');
    expect(asked.length).toBe(1);

    // Closing keeps the page state.
    button('Close', dialog)!.click();
    await settle();
    expect(page.categoriesOpen()).toBe(false);
    expect(text()).toContain('Public service requests are ready.');
  }, 20_000);

  it('offers the Category selector with an inactive current category, keeps it when categories fail and maps a 400 (FR-10, AC-15)', async () => {
    await setup('owner');
    const labels = (): string[] =>
      drawer()
        .categoryOptions()
        .map((option) => option.label);

    button('Add item')!.click();
    await settle();
    const form = drawer();
    expect(labels()).toEqual(['No category', 'Plumbing']);
    expect(form.categoryId()).toBeNull();
    expect(host.querySelector('label[for="item-category"]')?.textContent).toContain('Category');
    form.onTypeChange('service');
    form.onText('name', 'Drain clearing');
    form.onText('unitCost', '10');
    form.onText('unitPrice', '20');
    form.onCategoryChange('k-1');
    await settle();
    button('Save item')!.click();
    await settle();
    let request = call('POST', 'catalog-items');
    expect(request.request.body).toMatchObject({ categoryId: 'k-1' });
    request.flush({ status: 400, errors: { categoryId: ['server text'] } }, PROBLEM);
    await settle();
    expect(host.querySelector('#item-category-error')?.textContent?.trim()).toBe('server text');
    form.onCategoryChange(null);
    await settle();
    expect(host.querySelector('#item-category-error')).toBeNull();
    button('Save item')!.click();
    await settle();
    request = call('POST', 'catalog-items');
    expect(request.request.body).toMatchObject({ categoryId: null });
    request.flush(detail(), { status: 201, statusText: 'Created' });
    await settle();
    refresh();

    // Edit: the inactive current category is listed, selected and kept when categories fail to load.
    page.buildMenu(SINK)[0].command!({});
    await settle();
    call('GET', 'catalog-items/c-1').flush(INACTIVE_ITEM);
    await settle();
    expect(labels()).toEqual(['No category', 'Plumbing', 'Electrical (Inactive)']);
    expect(form.categoryId()).toBe('k-2');
    expect(form.dirty()).toBe(false);
    page.loadCategories();
    call('GET', 'catalog-categories').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    const drawerHost = host.querySelector('app-item-drawer')!;
    expect(text(drawerHost)).toContain("Couldn't load categories.");
    form.onText('unitPrice', '250');
    button('Retry', drawerHost)!.click();
    call('GET', 'catalog-categories').flush({ items: CATEGORIES });
    await settle();
    button('Save item')!.click();
    await settle();
    request = call('PUT', 'catalog-items/c-1');
    expect(request.request.body).toMatchObject({ categoryId: 'k-2' });
    request.flush(INACTIVE_ITEM);
    await settle();
    refresh();
  }, 20_000);

  it('shows the readiness note per status and hides it when the summary fails (FR-10, AC-16)', async () => {
    await setup('viewer');
    expect(host.querySelector('.catalog__note--success')?.textContent).toContain(
      'Public service requests are ready.',
    );
    expect(button('Manage categories')).toBeUndefined();

    for (const [status, copy] of [
      ['no_active_categories', 'Create and activate a category to accept public requests.'],
      [
        'no_active_services',
        'Assign an active service to an active category to accept public requests.',
      ],
    ]) {
      page.loadSummary();
      call('GET', 'catalog-items/summary').flush({ ...SUMMARY, publicRequestReadiness: status });
      await settle();
      expect(
        host.querySelector('.catalog__note--standalone.catalog__note--warning')?.textContent,
      ).toContain(copy);
      expect(host.querySelector('.catalog__note--success')).toBeNull();
    }

    page.loadSummary();
    call('GET', 'catalog-items/summary').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(host.querySelector('.catalog__note--standalone')).toBeNull();
  });

  it('confirms deactivation, activates without confirmation and handles a missing item (FR-09, AC-09)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');
    const asked = confirmations();

    page.buildMenu(SINK)[1].command!({});
    expect(asked[0]).toMatchObject({
      message:
        'Deactivate Kitchen sink leak repair? It stays on existing quotes, jobs and invoices, and can be reactivated at any time.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Deactivate' },
      rejectButtonProps: { label: 'Cancel' },
    });
    httpTesting.expectNone((r) => r.method === 'POST');
    asked[0].accept!();
    call('POST', 'catalog-items/c-1/deactivate').flush(null, NO_CONTENT);
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Kitchen sink leak repair deactivated',
    });
    refresh();

    page.buildMenu(VALVE)[1].command!({});
    call('POST', 'catalog-items/c-2/activate').flush(null, NO_CONTENT);
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Brass shut-off valve activated',
    });
    refresh();

    asked[0].accept!();
    call('POST', 'catalog-items/c-1/deactivate').flush(null, {
      status: 404,
      statusText: 'Not Found',
    });
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: 'This item is no longer available.',
    });
    refresh();
  });

  it('validates the item drawer, updates the margin live, maps a 409 and guards a dirty close (FR-08, AC-08)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');

    button('Add item')!.click();
    await settle();
    const form = drawer();
    expect(host.querySelector('.item-drawer__title')?.textContent?.trim()).toBe('Add item');
    expect(text()).toContain('Internal only. Used for profitability analysis.');
    expect(host.querySelector('#item-usage-title')).toBeNull();

    button('Save item')!.click();
    await settle();
    httpTesting.expectNone((r) => r.method === 'POST');
    expect(host.querySelector('#item-type-service-error')?.textContent?.trim()).toBe(
      'Select a type.',
    );
    expect(host.querySelector('#item-name-error')?.textContent?.trim()).toBe('Enter a name.');
    expect(host.querySelector('#item-unit-cost-error')?.textContent?.trim()).toBe(
      'Enter a unit cost of 0 or more.',
    );
    expect(host.querySelector('#item-unit-price-error')?.textContent?.trim()).toBe(
      'Enter a unit price of 0 or more.',
    );

    // Live margin (BR-09): half away from zero, "—" at price 0, negative allowed.
    const margin = () => text(host.querySelector('.item-drawer__margin')!);
    for (const [cost, price, percent, profit] of [
      ['90', '240', '62.5%', '$150.00'],
      ['100', '80', '-25.0%', '-$20.00'],
      ['1', '3', '66.7%', '$2.00'],
      ['10', '0', '—', '-$10.00'],
      ['abc', '5', '—', '—'],
    ]) {
      form.onText('unitCost', cost);
      form.onText('unitPrice', price);
      await settle();
      expect(margin()).toContain(percent);
      expect(margin()).toContain(profit);
    }
    expect(host.querySelector('.item-drawer__margin [aria-live="polite"]')).not.toBeNull();

    form.onTypeChange('service');
    form.onText('name', '  Kitchen   sink ');
    form.onText('unitCost', '90');
    form.onText('unitPrice', '240.5');
    await settle();
    button('Save item')!.click();
    await settle();
    let request = call('POST', 'catalog-items');
    expect(request.request.body).toEqual({
      type: 'service',
      name: 'Kitchen sink',
      description: null,
      categoryId: null,
      unitCost: 90,
      unitPrice: 240.5,
      isTaxable: true,
      isActive: true,
    });
    expect(form.submitting()).toBe(true);
    request.flush(
      { status: 409, errors: { name: ['server text'] } },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(host.querySelector('#item-name-error')?.textContent?.trim()).toBe(
      'An item with this name already exists for this type.',
    );
    expect(host.querySelector<HTMLInputElement>('#item-name')?.value).toBe('  Kitchen   sink ');
    expect(form.submitting()).toBe(false);

    // A dirty close asks; Keep editing leaves the drawer open.
    const asked = confirmations();
    button('Cancel')!.click();
    expect(asked[0]).toMatchObject({
      key: 'discard-changes',
      message:
        'You have unsaved changes in this item. If you leave now, those changes will be lost.',
    });
    expect(page.drawerOpen()).toBe(true);
    httpTesting.expectNone((r) => r.method === 'POST');

    button('Save item')!.click();
    await settle();
    call('POST', 'catalog-items').flush(detail(), { status: 201, statusText: 'Created' });
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Item added' });
    expect(page.drawerOpen()).toBe(false);
    refresh();

    // Edit mode loads the detail with usage; an unchanged drawer closes without asking.
    page.buildMenu(SINK)[0].command!({});
    await settle();
    expect(host.querySelector('#item-name')).toBeNull();
    call('GET', 'catalog-items/c-1').flush(detail());
    await settle();
    expect(host.querySelector('.item-drawer__title')?.textContent?.trim()).toBe('Edit item');
    expect(text()).toContain('Used on 1 quote, 14 jobs, and 0 invoices.');
    expect(margin()).toContain('62.5%');
    form.onText('unitPrice', '250');
    await settle();
    button('Save item')!.click();
    await settle();
    request = call('PUT', 'catalog-items/c-1');
    request.flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: 'This item is no longer available.',
    });
    expect(page.drawerOpen()).toBe(false);
    refresh();
  }, 15_000);

  it('stages the image until the item saves and keeps the drawer open in edit mode when the image fails (FR-08, AC-12)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');

    button('Add item')!.click();
    await settle();
    const form = drawer();
    form.onTypeChange('product');
    form.onText('name', 'Faucet');
    form.onText('unitCost', '22');
    form.onText('unitPrice', '39');

    await pick('.item-drawer__file', file('notes.gif', 'image/gif'));
    expect(host.querySelector('#item-image-error')?.textContent?.trim()).toBe(
      'Choose a PNG or JPG image.',
    );
    await pick('.item-drawer__file', file('big.png', 'image/png', 5_242_881));
    expect(host.querySelector('#item-image-error')?.textContent?.trim()).toBe(
      'Choose an image of 5 MB or smaller.',
    );
    await pick('.item-drawer__file', file('a.png', 'image/png'));
    expect(host.querySelector('#item-image-error')).toBeNull();
    expect(host.querySelector('img[alt="Item image preview"]')).not.toBeNull();
    expect(button('Replace')).toBeDefined();
    button('Remove')!.click();
    await settle();
    expect(text()).toContain('Drag and drop an image here or click to upload');
    expect(text()).toContain('PNG, JPG up to 5MB');
    await pick('.item-drawer__file', file('a.png', 'image/png'));

    // No image request until the item save succeeds (create: after 201).
    button('Save item')!.click();
    await settle();
    httpTesting.expectNone((r) => r.url.endsWith('/image'));
    call('POST', 'catalog-items').flush(detail({ id: 'c-9' }), {
      status: 201,
      statusText: 'Created',
    });
    await settle();
    const upload = call('PUT', 'catalog-items/c-9/image');
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.flush(
      { status: 400, errors: { file: ['Choose a PNG or JPG image.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    // The item stays saved: edit mode, image error under the image area, list refreshed.
    expect(page.drawerOpen()).toBe(true);
    expect(host.querySelector('.item-drawer__title')?.textContent?.trim()).toBe('Edit item');
    expect(host.querySelector('#item-image-error')?.textContent?.trim()).toBe(
      'Choose a PNG or JPG image.',
    );
    expect(messages).not.toHaveBeenCalledWith({ severity: 'success', summary: 'Item added' });
    refresh();

    // Saving again updates the saved item, then retries the staged image.
    button('Save item')!.click();
    await settle();
    call('PUT', 'catalog-items/c-9').flush(detail({ id: 'c-9' }));
    await settle();
    call('PUT', 'catalog-items/c-9/image').flush({
      contentType: 'image/png',
      sizeBytes: 10,
      updatedAt: '',
    });
    await settle();
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Item updated' });
    expect(page.drawerOpen()).toBe(false);
    refresh();
  }, 15_000);

  it('exports with the current filters, downloads the template and imports a CSV with row errors (FR-12 to FR-14, AC-13, AC-14, AC-17)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');
    const downloads: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });

    page.onType('product');
    page.onStatus('inactive');
    const listed = httpTesting.match((r) => r.url === `${API}/catalog-items`);
    listed[listed.length - 1].flush({ items: [VALVE], page: 1, pageSize: 10, totalCount: 1 });
    await settle();
    button('Export list')!.click();
    await settle();
    let request = httpTesting.expectOne((r) => r.url === `${API}/catalog-items/export`);
    expect(request.request.params.get('type')).toBe('product');
    expect(request.request.params.get('status')).toBe('inactive');
    expect(request.request.params.get('sort')).toBe('name');
    expect(request.request.params.has('page')).toBe(false);
    expect(button('Export list')!.disabled).toBe(true);
    request.flush(new Blob(['type,name'], { type: 'text/csv' }), {
      headers: { 'Content-Disposition': 'attachment; filename="products-services-2026-09-30.csv"' },
    });
    await settle();
    expect(downloads).toEqual(['products-services-2026-09-30.csv']);

    button('Export list')!.click();
    httpTesting
      .expectOne((r) => r.url === `${API}/catalog-items/export`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: "We couldn't export items. Try again.",
    });

    button('Download CSV')!.click();
    call('GET', 'catalog-items/import-template').flush(
      new Blob(['type,name'], { type: 'text/csv' }),
    );
    await settle();
    expect(downloads[1]).toBe('products-services-template.csv');

    button('Import CSV')!.click();
    await settle();
    const dialog = document.body;
    expect(text(dialog)).toContain('Import products & services');
    expect(text(dialog)).toContain('Start with the FieldOps template');
    await pick('#import-file', file('items.txt', 'text/plain'), dialog);
    expect(text(dialog)).toContain('Choose a CSV file.');
    expect(button('Import', dialog)!.disabled).toBe(true);

    await pick('#import-file', file('items.csv', 'text/csv'), dialog);
    button('Import', dialog)!.click();
    await settle();
    request = call('POST', 'catalog-items/import');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush(
      { status: 400, rowErrors: [{ row: 3, column: 'unit_price', message: 'Enter a price.' }] },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(importDialog().visible()).toBe(true);
    expect(text(dialog)).toContain('Row 3 · unit_price: Enter a price.');

    await pick('#import-file', file('fixed.csv', 'text/csv'), dialog);
    button('Import', dialog)!.click();
    await settle();
    call('POST', 'catalog-items/import').flush({ importedCount: 3 });
    await settle();
    expect(importDialog().visible()).toBe(false);
    expect(messages).toHaveBeenCalledWith({ severity: 'success', summary: 'Imported 3 items' });
    refresh();
  }, 15_000);
});
