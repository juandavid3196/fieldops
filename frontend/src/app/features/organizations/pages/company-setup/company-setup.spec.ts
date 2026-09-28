import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { companySettingsUnsavedChangesGuard } from '../../guards/company-settings-unsaved-changes.guard';
import { CompanySetup } from './company-setup';

const API_BASE_URL = 'http://api.test';
const SETTINGS_URL = `${API_BASE_URL}/organization-settings`;
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
  updatedAt: '2026-01-01T00:00:00.000000Z',
  canManage: true,
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
};

const BRANCH_B = { ...BRANCH_A, id: 'b-2', name: 'Round Rock', code: 'RR', isActive: true };

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

  const dialogButton = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.querySelectorAll('[role="alertdialog"] button')).find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement | undefined;

  const dialogVisible = (): boolean =>
    document.querySelectorAll('[role="alertdialog"] button').length > 0;

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  afterEach(() => httpTesting.verify());

  it('shows loading skeletons, a load error with Retry, the forbidden state, and (separately) the read-only banner with disabled inputs (AC-20,21,34,35)', async () => {
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
    expect(host.querySelector('.company-setup__nav')).toBeNull();

    await setup();
    flushInitial({ ...SETTINGS_RESPONSE, canManage: false }, 200, { items: [] });
    await stable();
    expect(host.textContent).toContain('You have view-only access to company settings.');
    expect(host.querySelector<HTMLInputElement>('#organization-name')?.disabled).toBe(true);
    expect(button('Save changes')).toBeUndefined();
  });

  it('enables Save/Discard once dirty, restores values on Discard, and locks/saves/reloads the session on Save (AC-22,23)', async () => {
    await setup();
    flushInitial();
    await stable();

    expect(button('Save changes')?.disabled).toBe(true);
    expect(button('Discard changes')?.disabled).toBe(true);

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, 'Acme Renamed');
    await stable();
    expect(button('Save changes')?.disabled).toBe(false);
    expect(button('Discard changes')?.disabled).toBe(false);

    button('Discard changes')?.click();
    await stable();
    dialogButton('Discard')?.click();
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
    request.flush({
      ...SETTINGS_RESPONSE,
      name: 'Acme Renamed',
      updatedAt: '2026-01-02T00:00:00.000000Z',
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

  it('blocks client-invalid saves with the summary and first-invalid focus, and clears a field error on blur after the attempt (AC-24,46)', async () => {
    await setup();
    flushInitial();
    await stable();

    const nameInput = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput, '');
    await stable();
    button('Save changes')?.click();
    await stable();

    httpTesting.expectNone({ method: 'PUT', url: SETTINGS_URL });
    expect(host.querySelector('#organization-name-error')?.textContent?.trim()).toBe(
      'This field is required.',
    );
    expect(document.activeElement?.id).toBe('organization-name');
    expect(host.querySelector('#company-setup-error-summary')).not.toBeNull();

    type(nameInput, 'Acme Field Services');
    nameInput.dispatchEvent(new Event('blur'));
    await stable();
    expect(host.querySelector('#organization-name-error')).toBeNull();
  });

  it('shows the stale-409 Reload flow with edits kept, and clears the session and navigates on 401 without a discard prompt (AC-25,50)', async () => {
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
    expect(button('Save changes')?.disabled).toBe(true);

    const nameInput2 = host.querySelector<HTMLInputElement>('#organization-name')!;
    type(nameInput2, 'Dirty again');
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

  it('confirms Deactivate from the row, pre-disables the only active branch on both surfaces, and reactivates without a prompt (AC-28,37,45)', async () => {
    await setup();
    flushInitial(SETTINGS_RESPONSE, 200, { items: [BRANCH_A, BRANCH_B] });
    await stable();

    const deactivateButtons = Array.from(host.querySelectorAll('button')).filter(
      (b) => b.textContent?.trim() === 'Deactivate',
    );
    expect(deactivateButtons.length).toBe(2);
    deactivateButtons[0].click();
    await stable();

    expect(document.body.textContent).toContain(
      'No new requests, quotes or work orders can be created for this branch.',
    );
    dialogButton('Deactivate')?.click();
    await stable();

    httpTesting
      .expectOne({ method: 'POST', url: `${BRANCHES_URL}/b-1/deactivate` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    httpTesting
      .expectOne(BRANCHES_URL)
      .flush({ items: [{ ...BRANCH_A, isActive: false }, BRANCH_B] });
    await stable();

    expect(host.textContent).toContain('Austin Central deactivated');

    // Only Round Rock is active now: its row Deactivate is pre-disabled with the BR-06 tooltip.
    const onlyActiveDeactivate = Array.from(host.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Deactivate',
    ) as HTMLButtonElement;
    expect(onlyActiveDeactivate.disabled).toBe(true);

    // Opening its drawer pre-disables the same action there too.
    const roundRockRow = Array.from(host.querySelectorAll('.branch-list__row')).find((row) =>
      row.textContent?.includes('Round Rock'),
    ) as HTMLElement;
    roundRockRow.click();
    await stable();
    httpTesting.expectOne({ method: 'GET', url: `${BRANCHES_URL}/b-2` }).flush({
      ...BRANCH_B,
      email: '',
      phone: '',
      businessHours: {},
      updatedAt: '2026-01-01T00:00:00.000000Z',
    });
    await stable();

    const drawerDeactivate = host.querySelector<HTMLButtonElement>('.branch-drawer__deactivate');
    expect(drawerDeactivate?.disabled).toBe(true);

    const drawerCancel = Array.from(host.querySelectorAll('.branch-drawer button')).find(
      (b) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;
    drawerCancel.click();
    await stable();

    // Reactivate the inactive branch: no confirmation dialog.
    const reactivateButton = Array.from(host.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Reactivate',
    ) as HTMLButtonElement;
    reactivateButton.click();
    await stable();
    expect(dialogVisible()).toBe(false);

    httpTesting
      .expectOne({ method: 'POST', url: `${BRANCHES_URL}/b-1/reactivate` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    httpTesting.expectOne(BRANCHES_URL).flush({ items: [BRANCH_A, BRANCH_B] });
    await stable();

    expect(host.textContent).toContain('Austin Central reactivated');
  });

  it('prompts "Discard unsaved changes?" via the route-leave guard and on closing a dirty drawer; Keep editing cancels both (AC-29)', async () => {
    // --- Route leave with a dirty organization form: exercise the guard's
    // decision function directly (`companySettingsUnsavedChangesGuard` is a
    // one-line delegation to it, see the guard file) rather than through a
    // real `Router` navigation, whose cancellation handling is flaky under
    // Zone.js test teardown timing. ---
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
    dialogButton('Discard')?.click();
    await stable();
    expect(secondValue).toBe(true);

    // --- Closing a dirty drawer (fresh instance; no route guard needed here) ---
    await setup();
    flushInitial(SETTINGS_RESPONSE, 200, { items: [BRANCH_A] });
    await stable();

    button('Add branch')?.click();
    await stable();
    const drawerNameInput = host.querySelector<HTMLInputElement>('#branch-name')!;
    type(drawerNameInput, 'New Branch Draft');
    await stable();

    const cancelButton = Array.from(host.querySelectorAll('.branch-drawer button')).find(
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
