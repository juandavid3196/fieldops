import { HttpEventType, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SequencesDialog } from '../../components/sequences-dialog/sequences-dialog';
import { companySettingsUnsavedChangesGuard } from '../../guards/company-settings-unsaved-changes.guard';
import { CompanySetup } from './company-setup';

const API_BASE_URL = 'http://api.test';
const SETTINGS_URL = `${API_BASE_URL}/organization-settings`;
const LOGO_URL = `${SETTINGS_URL}/logo`;
const BRANCHES_URL = `${API_BASE_URL}/branches`;
const SESSION_URL = `${API_BASE_URL}/sessions/current`;

const SETTINGS_RESPONSE = {
  name: 'Acme Field Services',
  legalName: 'Acme Field Services LLC',
  taxId: '',
  email: 'billing@acme.com',
  phone: '+1 555 111 2222',
  timezone: 'America/Chicago',
  currency: 'USD',
  defaultTaxRate: 8.25,
  quotePrefix: 'Q',
  workOrderPrefix: 'WO',
  invoicePrefix: 'INV',
  nextInvoiceNumber: 1050,
  website: 'www.acme.com',
  addressLine1: '100 Main St',
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78701',
  countryCode: 'US',
  pricesIncludeTax: false,
  nextQuoteNumber: 10,
  nextWorkOrderNumber: 20,
  hasInvoices: false,
  logo: null as unknown,
  updatedAt: '2026-01-01T00:00:00.000000Z',
  canManage: true,
};

const LOGO_METADATA = {
  contentType: 'image/png',
  sizeBytes: 1200,
  updatedAt: '2026-01-01T00:00:00.000000Z',
};

const BRANCH_A = {
  id: 'b-1',
  name: 'Austin Central',
  code: 'AUS-C',
  addressLine1: '100 Main St',
  addressLine2: '',
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78701',
  countryCode: 'US',
  timezone: 'America/Chicago',
  isActive: true,
  isMain: true,
  technicianCount: 1,
};

const BRANCH_B = {
  ...BRANCH_A,
  id: 'b-2',
  name: 'Round Rock',
  code: 'RR',
  isMain: false,
  technicianCount: 3,
};

@Component({ template: '<p>Sign in stub</p>' })
class SignInStub {}

@Component({ template: '<p>Other page</p>' })
class OtherStub {}

describe('CompanySetup', () => {
  let httpTesting: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;
  let host: HTMLElement;

  async function setup(withGuard = false): Promise<void> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          {
            path: 'admin/company',
            component: CompanySetup,
            ...(withGuard ? { canDeactivate: [companySettingsUnsavedChangesGuard] } : {}),
          },
          { path: 'auth/sign-in', component: SignInStub },
          { path: 'coming-soon/:module', component: OtherStub },
          { path: 'other', component: OtherStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/company', CompanySetup);
    await stable();
    host = harness.routeNativeElement as HTMLElement;
  }

  async function stable(): Promise<void> {
    await harness.fixture.whenStable();
  }

  const page = () => harness.routeDebugElement!.componentInstance as CompanySetup;

  function flushInitial(
    settings: Record<string, unknown> = SETTINGS_RESPONSE,
    settingsStatus = 200,
    branches: Record<string, unknown> = { items: [BRANCH_A, BRANCH_B] },
  ): void {
    httpTesting.expectOne(SETTINGS_URL).flush(settings, {
      status: settingsStatus,
      statusText: settingsStatus === 200 ? 'OK' : 'Error',
    });
    httpTesting.expectOne(BRANCHES_URL).flush(branches);
  }

  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as
      HTMLButtonElement | undefined;

  const bodyButton = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as
      HTMLButtonElement | undefined;

  const dialogButton = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.querySelectorAll('[role="alertdialog"] button')).find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement | undefined;

  const dialogVisible = (): boolean =>
    document.querySelectorAll('[role="alertdialog"] button').length > 0;

  /** Opens a row menu and clicks an item by its label. */
  async function rowMenu(branchName: string, itemLabel: string): Promise<void> {
    host.querySelector<HTMLButtonElement>(`[aria-label="More actions for ${branchName}"]`)!.click();
    await stable();
    const item = Array.from(document.querySelectorAll('[role="menuitem"]')).find(
      (li) => li.textContent?.trim() === itemLabel,
    );
    item?.querySelector<HTMLElement>('a')?.click();
    await stable();
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  beforeEach(() => {
    Element.prototype.scrollIntoView = vi.fn();
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => httpTesting.verify());

  it('shows loading skeletons, a load error with Retry, the forbidden state, and the read-only banner with disabled inputs (AC-20)', async () => {
    await setup();
    expect(host.querySelectorAll('p-skeleton').length).toBeGreaterThan(0);

    flushInitial({ title: 'Error', status: 500 }, 500, { items: [] });
    await stable();
    expect(host.textContent).toContain("We couldn't load company settings.");

    button('Retry')?.click();
    await stable();
    httpTesting
      .expectOne(SETTINGS_URL)
      .flush({ title: 'Forbidden', status: 403 }, { status: 403, statusText: 'Forbidden' });
    await stable();
    expect(host.textContent).toContain("You don't have access to company settings.");
    expect(host.querySelector('#organization-name')).toBeNull();
    expect(host.querySelector('app-administration-nav')).toBeNull();

    await setup();
    flushInitial({ ...SETTINGS_RESPONSE, canManage: false }, 200, { items: [] });
    await stable();
    expect(host.textContent).toContain(
      'You have view-only access to company settings. Contact an Owner to request changes.',
    );
    expect(host.textContent).toContain(
      'Manage your organization details, branches, billing defaults, and document numbering.',
    );
    expect(host.querySelector<HTMLInputElement>('#organization-name')?.disabled).toBe(true);
    expect(button('Save changes')).toBeUndefined();
  });

  it('enables Save/Discard once dirty, restores values on Discard, and sends normalized values once on Save (AC-07, AC-22)', async () => {
    await setup();
    flushInitial();
    await stable();

    expect(button('Save changes')?.disabled).toBe(true);
    expect(button('Discard changes')?.disabled).toBe(true);

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, 'Acme Renamed');
    await stable();
    expect(button('Save changes')?.disabled).toBe(false);

    button('Discard changes')?.click();
    await stable();
    dialogButton('Discard changes')?.click();
    await stable();
    expect(nameInput.value).toBe('Acme Field Services');
    expect(button('Save changes')?.disabled).toBe(true);

    type(nameInput, 'Acme Renamed');
    await stable();
    button('Save changes')?.click();
    await stable();

    expect(nameInput.readOnly).toBe(true);
    button('Save changes')?.click();
    await stable();

    // Exactly one request: a second click while submitting sent nothing further.
    const request = httpTesting.expectOne({ method: 'PUT', url: SETTINGS_URL });
    expect(request.request.body).toMatchObject({
      name: 'Acme Renamed',
      website: 'www.acme.com',
      countryCode: 'US',
      stateRegion: 'TX',
      pricesIncludeTax: false,
      nextQuoteNumber: 10,
    });
    expect(request.request.body).not.toHaveProperty('confirmCurrencyChange');
    request.flush({
      ...SETTINGS_RESPONSE,
      name: 'Acme Renamed',
      updatedAt: '2026-01-02T00:00:00Z',
    });
    await stable();

    httpTesting.expectOne(SESSION_URL).flush({
      user: { id: 'u-1', firstName: 'Jane', lastName: 'Doe', email: 'owner@acme.com' },
      organization: { id: 'o-1', name: 'Acme Renamed' },
      role: { code: 'owner', name: 'Owner' },
    });
    await stable();

    expect(host.textContent).toContain('Company settings saved');
    expect(button('Save changes')?.disabled).toBe(true);
  });

  it('blocks client-invalid saves (required, website, US state) with the summary and first-invalid focus (AC-08)', async () => {
    await setup();
    flushInitial();
    await stable();

    type(host.querySelector<HTMLInputElement>('#organization-name')!, '');
    type(host.querySelector<HTMLInputElement>('#organization-website')!, 'not a site');
    type(host.querySelector<HTMLInputElement>('#organization-city')!, '');
    page().form.controls.stateRegion.setValue('');
    await stable();
    button('Save changes')?.click();
    await stable();

    httpTesting.expectNone({ method: 'PUT', url: SETTINGS_URL });
    expect(host.querySelector('#organization-name-error')?.textContent?.trim()).toBe(
      'This field is required.',
    );
    expect(host.querySelector('#organization-website-error')?.textContent?.trim()).toBe(
      'Enter a valid website.',
    );
    expect(host.querySelector('#organization-city-error')?.textContent).toContain(
      'This field is required.',
    );
    expect(host.querySelector('#organization-stateRegion-error')?.textContent).toContain(
      'Select a state.',
    );
    expect(document.activeElement?.id).toBe('organization-name');
    expect(host.querySelector('#company-setup-error-summary')).not.toBeNull();

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, 'Acme Field Services');
    nameInput.dispatchEvent(new Event('blur'));
    await stable();
    expect(host.querySelector('#organization-name-error')).toBeNull();
  });

  it('shows the stale-409 Reload flow with edits kept, and clears the session on 401 without a discard prompt', async () => {
    await setup();
    flushInitial();
    await stable();

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, 'Acme Renamed');
    await stable();
    button('Save changes')?.click();
    await stable();

    httpTesting
      .expectOne({ method: 'PUT', url: SETTINGS_URL })
      .flush({ status: 409 }, { status: 409, statusText: 'Conflict' });
    await stable();

    expect(host.textContent).toContain(
      'This record was changed by someone else. Reload to see the latest version.',
    );
    expect(nameInput.value).toBe('Acme Renamed');

    button('Reload')?.click();
    await stable();
    httpTesting.expectOne(SETTINGS_URL).flush({ ...SETTINGS_RESPONSE, name: 'Acme From Server' });
    await stable();
    expect(host.querySelector<HTMLInputElement>('#organization-name')?.value).toBe(
      'Acme From Server',
    );

    type(host.querySelector<HTMLInputElement>('#organization-name')!, 'Dirty again');
    await stable();
    button('Save changes')?.click();
    await stable();
    httpTesting
      .expectOne({ method: 'PUT', url: SETTINGS_URL })
      .flush({ title: 'Unauthorized', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await stable();

    expect(router.url).toBe('/auth/sign-in');
    expect(dialogVisible()).toBe(false);
  });

  it('renders the Administration column anchors and Coming soon items (FR-05, AC-06)', async () => {
    await setup();
    flushInitial();
    await stable();

    const nav = host.querySelector('nav[aria-label="Administration"]')!;
    const links = () => Array.from(nav.querySelectorAll('a'));
    expect(links().map((link) => link.textContent?.trim())).toEqual([
      'Company profile',
      'Branches',
      'Business hours',
      'Users & permissions',
      'Products & services',
      'Taxes & currency',
      'Document numbering',
      'Notifications',
    ]);
    expect(links()[0].getAttribute('aria-current')).toBe('location');

    links()[5].click();
    await stable();
    expect(links()[5].getAttribute('aria-current')).toBe('location');
    expect(links()[0].getAttribute('aria-current')).toBeNull();
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled();
    expect(router.url).toBe('/admin/company');

    links()[2].click();
    await stable();
    expect(router.url).toBe('/coming-soon/business-hours');
  });

  it('uploads, previews, replaces and removes the logo without dirtying the form (FR-10, AC-13)', async () => {
    await setup();
    flushInitial();
    await stable();
    expect(host.textContent).toContain('No logo');
    expect(host.textContent).toContain('JPG, PNG or SVG. Max 2 MB.');

    const pick = async (file: File): Promise<void> => {
      const input = host.querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', {
        value: { item: () => file, length: 1 },
        configurable: true,
      });
      input.dispatchEvent(new Event('change'));
      await stable();
    };

    await pick(new File(['x'], 'logo.gif', { type: 'image/gif' }));
    expect(host.querySelector('#organization-logo-error')?.textContent).toContain(
      'Choose a JPG, PNG or SVG file.',
    );
    await pick(new File([new Uint8Array(3 * 1024 * 1024)], 'big.png', { type: 'image/png' }));
    expect(host.querySelector('#organization-logo-error')?.textContent).toContain(
      'Choose a file of 2 MB or smaller.',
    );
    httpTesting.expectNone({ method: 'PUT', url: LOGO_URL });

    await pick(new File([new Uint8Array(10)], 'logo.png', { type: 'image/png' }));
    const upload = httpTesting.expectOne({ method: 'PUT', url: LOGO_URL });
    expect(upload.request.body instanceof FormData).toBe(true);
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.event({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    await stable();
    expect(host.querySelector('[role="progressbar"]')).not.toBeNull();
    upload.flush(LOGO_METADATA);
    await stable();

    expect(host.querySelector('img')?.getAttribute('alt')).toBe('Acme Field Services logo');
    expect(host.textContent).toContain('Logo updated');
    expect(host.querySelector('#organization-logo-error')).toBeNull();
    expect(button('Save changes')?.disabled).toBe(true);

    button('Remove logo')?.click();
    await stable();
    httpTesting
      .expectOne({ method: 'DELETE', url: LOGO_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    expect(host.querySelector('img')).toBeNull();
    expect(host.textContent).toContain('No logo');
    expect(host.textContent).toContain('Logo removed');
    expect(button('Save changes')?.disabled).toBe(true);
  });

  it.each([
    ['confirms before sending when invoices exist', true],
    ['sends first and confirms after a server 409', false],
  ])('asks to confirm a currency change and %s (FR-08, AC-10)', async (_name, hasInvoices) => {
    await setup();
    flushInitial({ ...SETTINGS_RESPONSE, hasInvoices });
    await stable();

    page().form.markAsDirty();
    page().form.controls.currency.setValue('EUR');
    await stable();

    const save = async () => {
      button('Save changes')?.click();
      await stable();
    };
    const dialogText = 'Change currency to EUR? Existing invoices keep their original currency.';

    if (hasInvoices) {
      await save();
      httpTesting.expectNone({ method: 'PUT', url: SETTINGS_URL });
      expect(document.body.textContent).toContain(dialogText);
      dialogButton('Cancel')?.click();
      await stable();
      expect(page().form.controls.currency.value).toBe('EUR');
      await save();
    } else {
      await save();
      const first = httpTesting.expectOne({ method: 'PUT', url: SETTINGS_URL });
      expect(first.request.body).not.toHaveProperty('confirmCurrencyChange');
      first.flush(
        { status: 409, errors: { currency: ['Confirm the currency change.'] } },
        { status: 409, statusText: 'Conflict' },
      );
      await stable();
      expect(document.body.textContent).toContain(dialogText);
      expect(page().form.controls.currency.value).toBe('EUR');
    }

    dialogButton('Change currency')?.click();
    await stable();
    const confirmed = httpTesting.expectOne({ method: 'PUT', url: SETTINGS_URL });
    expect(confirmed.request.body).toMatchObject({ currency: 'EUR', confirmCurrencyChange: true });
    confirmed.flush({ ...SETTINGS_RESPONSE, hasInvoices, currency: 'EUR' });
    await stable();
    httpTesting.expectOne(SESSION_URL).flush({
      user: { id: 'u-1', firstName: 'Jane', lastName: 'Doe', email: 'owner@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: 'owner', name: 'Owner' },
    });
    await stable();
  });

  it('binds the Prices include tax switch and edits sequences through the dialog, routing server errors to it (FR-11, FR-12, AC-19)', async () => {
    await setup();
    flushInitial();
    await stable();

    const taxSwitch = host.querySelector<HTMLInputElement>('#organization-pricesIncludeTax')!;
    expect(taxSwitch.getAttribute('role')).toBe('switch');
    taxSwitch.click();
    await stable();
    expect(page().form.controls.pricesIncludeTax.value).toBe(true);
    expect(button('Save changes')?.disabled).toBe(false);
    expect(host.querySelector('a[href="/coming-soon/tax-rates"]')).not.toBeNull();

    button('Edit sequences')?.click();
    await stable();
    const dialog = harness.fixture.debugElement.query(By.directive(SequencesDialog))
      .componentInstance as SequencesDialog;
    expect(document.body.textContent).toContain('Edit sequences');
    expect(dialog.form.getRawValue()).toEqual({
      nextQuoteNumber: 10,
      nextWorkOrderNumber: 20,
      nextInvoiceNumber: 1050,
    });

    // Cancel discards; an invalid value shows a field message; Apply copies valid values.
    dialog.form.patchValue({ nextQuoteNumber: 99 });
    bodyButton('Cancel')?.click();
    await stable();
    expect(page().form.controls.nextQuoteNumber.value).toBe(10);

    button('Edit sequences')?.click();
    await stable();
    dialog.form.patchValue({ nextQuoteNumber: null });
    bodyButton('Apply')?.click();
    await stable();
    expect(document.body.textContent).toContain('This field is required.');
    dialog.form.patchValue({ nextQuoteNumber: 15 });
    bodyButton('Apply')?.click();
    await stable();
    expect(page().form.controls.nextQuoteNumber.value).toBe(15);
    expect(page().form.dirty).toBe(true);

    // A server 400 for the quote number: summary is plain text; focus goes to the Edit sequences link.
    button('Save changes')?.click();
    await stable();
    httpTesting.expectOne({ method: 'PUT', url: SETTINGS_URL }).flush(
      {
        status: 400,
        errors: { nextQuoteNumber: ['Enter a number greater than the last quote number.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await stable();
    expect(document.activeElement?.textContent).toContain('Edit sequences');
    const summary = host.querySelector('#company-setup-error-summary')!;
    expect(summary.querySelector('a')).toBeNull();
    expect(summary.textContent).toContain(
      'Next quote number: Enter a number greater than the last quote number.',
    );
  });

  it('shows the Main branch tag, Team and generic time zone, and runs Set as main and Deactivate from the row menu (FR-13, FR-15, AC-14, AC-16, AC-17)', async () => {
    await setup();
    flushInitial();
    await stable();

    const rows = Array.from(host.querySelectorAll('tbody tr'));
    expect(rows[0].textContent).toContain('Austin Central');
    expect(rows[0].textContent).toContain('Main branch');
    expect(rows[0].textContent).toContain('1 technician');
    expect(rows[0].textContent).not.toContain('1 technicians');
    expect(rows[0].textContent).toContain('Central Time');
    expect(rows[1].textContent).toContain('3 technicians');
    expect(rows[1].textContent).not.toContain('Main branch');

    // Set as main: Round Rock becomes main after the list reloads.
    await rowMenu('Round Rock', 'Set as main branch');
    httpTesting
      .expectOne({ method: 'POST', url: `${BRANCHES_URL}/b-2/set-main` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    httpTesting.expectOne(BRANCHES_URL).flush({
      items: [
        { ...BRANCH_A, isMain: false },
        { ...BRANCH_B, isMain: true },
      ],
    });
    await stable();
    expect(host.textContent).toContain('Round Rock is now the main branch');

    // Deactivate is disabled for the main branch; enabled (with confirmation) for the other.
    await rowMenu('Round Rock', 'Deactivate');
    httpTesting.expectNone({ method: 'POST', url: `${BRANCHES_URL}/b-2/deactivate` });
    expect(dialogVisible()).toBe(false);
    document.body.click();
    await stable();

    await rowMenu('Austin Central', 'Deactivate');
    expect(document.body.textContent).toContain(
      'No new requests, quotes or work orders can be created for this branch.',
    );
    dialogButton('Deactivate')?.click();
    await stable();
    httpTesting
      .expectOne({ method: 'POST', url: `${BRANCHES_URL}/b-1/deactivate` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    httpTesting.expectOne(BRANCHES_URL).flush({
      items: [
        { ...BRANCH_A, isMain: false, isActive: false },
        { ...BRANCH_B, isMain: true },
      ],
    });
    await stable();
    expect(host.textContent).toContain('Austin Central deactivated');

    // Reactivate needs no confirmation.
    await rowMenu('Austin Central', 'Reactivate');
    expect(dialogVisible()).toBe(false);
    httpTesting
      .expectOne({ method: 'POST', url: `${BRANCHES_URL}/b-1/reactivate` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    httpTesting.expectOne(BRANCHES_URL).flush({ items: [BRANCH_A, BRANCH_B] });
    await stable();
  });

  it('shows every new control read-only for a Viewer (FR-17, AC-20)', async () => {
    await setup();
    flushInitial({ ...SETTINGS_RESPONSE, canManage: false, logo: LOGO_METADATA });
    await stable();
    httpTesting
      .expectOne({ method: 'GET', url: LOGO_URL })
      .flush(new Blob(['x'], { type: 'image/png' }));
    await stable();

    expect(host.querySelector('img')).not.toBeNull();
    for (const label of [
      'Change logo',
      'Remove logo',
      'Save changes',
      'Discard changes',
      'Add branch',
    ]) {
      expect(button(label), label).toBeUndefined();
    }
    expect(host.querySelector<HTMLInputElement>('#organization-pricesIncludeTax')?.disabled).toBe(
      true,
    );

    // Sequences: "View sequences" opens a read-only dialog with only Close.
    button('View sequences')?.click();
    await stable();
    expect(document.body.textContent).toContain('Sequences');
    expect(bodyButton('Apply')).toBeUndefined();
    expect(bodyButton('Cancel')).toBeUndefined();
    expect(bodyButton('Close')).toBeDefined();
    expect(document.querySelector<HTMLInputElement>('#sequence-nextQuoteNumber')?.disabled).toBe(
      true,
    );
    bodyButton('Close')?.click();
    await stable();

    // Row menu offers only View; the drawer footer only Close and its switch is disabled.
    await rowMenu('Round Rock', 'View');
    httpTesting.expectOne({ method: 'GET', url: `${BRANCHES_URL}/b-2` }).flush({
      ...BRANCH_B,
      email: '',
      phone: '',
      businessHours: {},
      servicePostalCodes: [],
      usesCompanyBilling: true,
      updatedAt: '2026-01-01T00:00:00.000000Z',
    });
    await stable();
    expect(host.querySelector<HTMLInputElement>('#branch-usesCompanyBilling')?.disabled).toBe(true);
    const footerButtons = Array.from(host.querySelectorAll('.drawer-shell__footer button'), (b) =>
      b.textContent?.trim(),
    );
    expect(footerButtons).toEqual(['Close']);
  });

  it('prompts "Discard unsaved changes?" via the route-leave guard and on closing a dirty drawer; Keep editing cancels both', async () => {
    await setup();
    flushInitial(SETTINGS_RESPONSE, 200, { items: [BRANCH_A] });
    await stable();
    const component = harness.routeDebugElement!.componentInstance as CompanySetup;

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, 'Acme Renamed');
    await stable();

    const firstResult = component.canLeave();
    expect(typeof firstResult).not.toBe('boolean');
    let firstValue: boolean | undefined;
    (firstResult as Observable<boolean>).subscribe((value) => (firstValue = value));
    await stable();
    expect(document.body.textContent).toContain('Discard unsaved changes?');

    dialogButton('Keep editing')?.click();
    await stable();
    expect(firstValue).toBe(false);
    expect(dialogVisible()).toBe(false);
    expect(nameInput.value).toBe('Acme Renamed');

    const secondResult = component.canLeave();
    let secondValue: boolean | undefined;
    (secondResult as Observable<boolean>).subscribe((value) => (secondValue = value));
    await stable();
    dialogButton('Discard changes')?.click();
    await stable();
    expect(secondValue).toBe(true);

    // Closing a dirty drawer (fresh instance).
    await setup();
    flushInitial(SETTINGS_RESPONSE, 200, { items: [BRANCH_A] });
    await stable();

    button('Add branch')?.click();
    await stable();
    type(host.querySelector<HTMLInputElement>('#branch-name')!, 'New Branch Draft');
    await stable();

    expect(
      Array.from(host.querySelectorAll('.drawer-shell__footer button'), (b) =>
        b.textContent?.trim(),
      ),
    ).toEqual(['Cancel', 'Create branch']);
    const cancelButton = Array.from(host.querySelectorAll('.drawer-shell__footer button')).find(
      (b) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;
    cancelButton.click();
    await stable();
    expect(dialogVisible()).toBe(true);

    dialogButton('Keep editing')?.click();
    await stable();
    expect(host.querySelector<HTMLInputElement>('#branch-name')?.value).toBe('New Branch Draft');
  });
});
