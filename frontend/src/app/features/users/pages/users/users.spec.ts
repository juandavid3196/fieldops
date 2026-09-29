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
import { Observable } from 'rxjs';
import { Confirmation, ConfirmationService, MenuItem, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { UserDrawer } from '../../components/user-drawer/user-drawer';
import { PermissionMatrix, UserRow } from '../../models/users.model';
import { Users } from './users';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

const row = (over: Partial<UserRow>): UserRow => ({
  id: 'm-1',
  kind: 'member',
  firstName: 'Alex',
  lastName: 'Morgan',
  email: 'alex@acme.com',
  roleCode: 'owner',
  roleName: 'Owner',
  isAllBranches: true,
  branches: [],
  teamProfile: { applicable: false, name: null },
  status: 'active',
  isExpired: false,
  lastActiveAt: null,
  isCurrentUser: false,
  isLastOwner: false,
  ...over,
});

const ALEX = row({ isCurrentUser: true, isLastOwner: true });
const DANIEL = row({
  id: 'm-3',
  firstName: 'Daniel',
  lastName: 'Kim',
  email: 'daniel@acme.com',
  roleCode: 'dispatcher',
  roleName: 'Dispatcher',
  isAllBranches: false,
  branches: [{ id: 'b-1', name: 'Austin Central' }],
  teamProfile: { applicable: true, name: 'Daniel Kim' },
});
const SAM = row({
  id: 'm-4',
  firstName: 'Sam',
  lastName: 'Lee',
  email: 'sam@acme.com',
  roleCode: 'technician',
  roleName: 'Technician',
  status: 'suspended',
  teamProfile: { applicable: true, name: null },
});
const OLIVIA = row({
  id: 'i-1',
  kind: 'invitation',
  firstName: 'Olivia',
  lastName: 'Carter',
  email: 'olivia@acme.com',
  roleCode: 'accounting',
  roleName: 'Accounting',
  status: 'pending_invitation',
  isExpired: true,
});
const ROWS = [ALEX, DANIEL, SAM, OLIVIA];

const ROLE_NAMES: Record<string, string> = {
  owner: 'Owner',
  operations_manager: 'Operations Manager',
  dispatcher: 'Dispatcher',
  technician: 'Technician',
  accounting: 'Accounting',
  viewer: 'Viewer',
};
const MATRIX: PermissionMatrix = {
  roles: Object.entries(ROLE_NAMES).map(([code, name]) => ({
    code,
    name,
    summary: `${name} summary from API`,
    forcesAllBranches: code === 'owner' || code === 'operations_manager',
    hasTeamProfile: ['technician', 'dispatcher', 'operations_manager'].includes(code),
  })),
  modules: [
    {
      key: 'overview',
      name: 'Overview',
      levels: Object.fromEntries(
        Object.keys(ROLE_NAMES).map((code) => [
          code,
          code === 'owner'
            ? { level: 'full', label: 'Full' }
            : code === 'viewer'
              ? { level: 'view_if_granted', label: 'View if granted' }
              : { level: 'view', label: 'View' },
        ]),
      ),
    },
    {
      key: 'company',
      name: 'Company settings',
      levels: Object.fromEntries(
        Object.keys(ROLE_NAMES).map((code) => [
          code,
          code === 'owner' ? { level: 'full', label: 'Full' } : { level: 'none', label: 'None' },
        ]),
      ),
    },
  ],
};
const BRANCHES = {
  items: [
    { id: 'b-1', name: 'Austin Central', isActive: true },
    { id: 'b-2', name: 'North Austin', isActive: true },
    { id: 'b-3', name: 'Closed', isActive: false },
  ],
};
const SUMMARY = { activeUsers: 21, pendingInvitations: 2, suspendedUsers: 1, owners: 1 };

describe('Users page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<Users>;
  let page: Users;
  let host: HTMLElement;
  let router: Router;

  const list = (): TestRequest => httpTesting.expectOne((r) => r.url === `${API}/users`);
  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const flushList = (items: readonly UserRow[] = ROWS, total = items.length): void =>
    list().flush({ items, page: 1, pageSize: 10, totalCount: total });

  async function setup(roleCode: string, initial: 'ok' | 'none' | 'list-401' = 'ok') {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'auth/sign-in', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    call('GET', 'sessions/current').flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: roleCode },
    });

    fixture = TestBed.createComponent(Users);
    page = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    if (initial === 'ok') {
      flushList();
      call('GET', 'users/summary').flush(SUMMARY);
      call('GET', 'permission-matrix').flush(MATRIX);
      call('GET', 'branches').flush(BRANCHES);
    } else if (initial === 'list-401') {
      list().flush(null, { status: 401, statusText: 'Unauthorized' });
      call('GET', 'users/summary').flush(SUMMARY);
      call('GET', 'permission-matrix').flush(MATRIX);
      call('GET', 'branches').flush(BRANCHES);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (): string => host.textContent?.replace(/\s+/g, ' ') ?? '';
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const drawer = (): UserDrawer =>
    fixture.debugElement.query(By.directive(UserDrawer)).componentInstance;
  const inject = <T>(token: new (...args: never[]) => T): T =>
    fixture.debugElement.injector.get(token);
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  afterEach(() => httpTesting.verify());

  it('renders the Owner page from the API: metrics, seven columns, statuses, matrix and legend (FR-01, FR-05, AC-01, AC-05)', async () => {
    await setup('owner');

    expect(host.querySelector('h1')?.textContent?.trim()).toBe('Users & permissions');
    expect(
      host.querySelector('nav[aria-label="Administration"] [aria-current="page"]')?.textContent,
    ).toContain('Users & permissions');
    expect(host.querySelector('nav[aria-label="Breadcrumb"]')?.textContent).toContain(
      'Administration',
    );
    expect(button('Invite user')).toBeDefined();
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['21', '2', '1', '1']);
    expect(
      Array.from(host.querySelectorAll('app-user-table thead th'), (n) => n.textContent?.trim()),
    ).toEqual([
      'User',
      'Role',
      'Branch access',
      'Linked team profile',
      'Status',
      'Last active',
      'Actions',
    ]);
    expect(host.querySelectorAll('app-user-table tbody tr')).toHaveLength(4);
    expect(host.querySelector('.user-table__expired')?.textContent?.trim()).toBe('Expired');
    expect(text()).toContain('Pending invitation');
    expect(text()).toContain('Not linked');
    expect(text()).toContain('Never');
    expect(text()).toContain('Showing 1–4 of 4 users');
    expect(host.querySelector('button[aria-label="More actions for Daniel Kim"]')).not.toBeNull();

    const matrix = host.querySelector('app-permission-matrix')!;
    expect(
      Array.from(matrix.querySelectorAll('th[scope="col"]'), (n) => n.textContent?.trim()),
    ).toEqual(['Module', ...Object.values(ROLE_NAMES)]);
    expect(
      Array.from(matrix.querySelectorAll('th[scope="row"]'), (n) => n.textContent?.trim()),
    ).toEqual(['Overview', 'Company settings']);
    expect(matrix.querySelectorAll('tbody .matrix__level--warning')[0].textContent?.trim()).toBe(
      'View if granted',
    );
    expect(matrix.querySelectorAll('tbody .matrix__level--neutral')).toHaveLength(5);
    expect(matrix.querySelector('.matrix__legend')?.textContent).toContain('scoped subset');
  });

  it.each([
    ['viewer', 'read-only'],
    ['dispatcher', 'forbidden'],
    ['owner', 'unauthorized'],
  ] as const)('handles the %s session as %s (FR-15, AC-17)', async (role, outcome) => {
    await setup(
      role,
      outcome === 'forbidden' ? 'none' : outcome === 'unauthorized' ? 'list-401' : 'ok',
    );

    if (outcome === 'read-only') {
      expect(text()).toContain('You have view-only access.');
      expect(button('Invite user')).toBeUndefined();
      expect(host.querySelectorAll('.user-table__actions')).toHaveLength(4);
      expect(text().match(/View only/g)).toHaveLength(4);
      expect(host.querySelector('button[aria-label^="More actions"]')).toBeNull();
      expect(host.querySelector('app-user-drawer')).toBeNull();
      expect(host.querySelector('app-permission-matrix table')).not.toBeNull();
    } else if (outcome === 'forbidden') {
      // No data request was issued: `verify()` in afterEach fails on any pending one.
      expect(text()).toContain("You don't have access to users and permissions.");
      expect(host.querySelector('nav[aria-label="Administration"]')).toBeNull();
      expect(host.querySelector('app-user-table')).toBeNull();
    } else {
      await vi.waitFor(() => expect(router.url).toBe('/auth/sign-in'));
    }
  });

  it('sends filters, sorting and paging to the API and shows empty, error and partial-metrics states (FR-03, FR-04, AC-04)', async () => {
    await setup('owner');

    const search = host.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'ali';
    search.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 350));
    let request = list();
    expect(request.request.params.get('search')).toBe('ali');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    await settle();
    expect(text()).toContain('No users match these filters.');
    expect(host.querySelector('app-user-table')).toBeNull();

    button('Clear filters')!.click();
    request = list();
    expect(request.request.params.has('search')).toBe(false);
    expect(request.request.params.get('sort')).toBe('name');
    request.flush({ items: ROWS, page: 1, pageSize: 10, totalCount: 24 });
    await settle();
    expect(text()).toContain('Showing 1–10 of 24 users');

    page.onRoleFilter('dispatcher');
    page.onBranchFilter('b-2');
    page.onStatusFilter('pending_invitation');
    page.toggleSort();
    page.onPageChange({ page: 1 });
    // switchMap keeps only the latest request; earlier ones are cancelled.
    const requests = httpTesting.match((r) => r.url === `${API}/users`);
    const latest = requests[requests.length - 1].request.params;
    expect(latest.get('roleCode')).toBe('dispatcher');
    expect(latest.get('branchId')).toBe('b-2');
    expect(latest.get('status')).toBe('pending_invitation');
    expect(latest.get('sort')).toBe('-name');
    expect(latest.get('page')).toBe('2');
    expect(requests.slice(0, -1).every((r) => r.cancelled)).toBe(true);
    requests[requests.length - 1].flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    await settle();

    page.clearFilters();
    flushList();
    await settle();

    // A failed list replaces the table with the error and Retry; Retry succeeds.
    page.loadList();
    list().flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load users. Check your connection and try again.");
    button('Retry')!.click();
    flushList([ALEX]);
    await settle();
    expect(host.querySelector('app-user-table')).not.toBeNull();

    // Failed metrics keep the table and show "—" with "Couldn't load".
    page.loadSummary();
    call('GET', 'users/summary').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(
      Array.from(host.querySelectorAll('.metrics__value'), (n) => n.textContent?.trim()),
    ).toEqual(['—', '—', '—', '—']);
    expect(host.querySelector('.metrics__value')?.getAttribute('aria-label')).toBe(
      "Active users: Couldn't load",
    );
    expect(host.querySelector('app-user-table')).not.toBeNull();
  });

  it('applies the invite drawer rules, validation and server error mapping (FR-07, AC-08)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');

    button('Invite user')!.click();
    await settle();
    const form = drawer();
    expect(host.querySelector('.user-drawer__title')?.textContent?.trim()).toBe('Invite user');
    expect(host.querySelector('#invite-team-link')).toBeNull();

    // BR-19: forced roles check and lock every branch; team-link only for team-profile roles.
    form.onRoleChange('owner');
    await settle();
    expect(form.allChecked()).toBe(true);
    expect(host.querySelector<HTMLInputElement>('#invite-branch-b-1')?.disabled).toBe(true);
    expect(host.textContent).toContain('Owner role summary');
    expect(host.textContent).toContain('Owner summary from API');
    expect(host.querySelector('#invite-team-link')).toBeNull();

    form.onRoleChange('dispatcher');
    await settle();
    expect(form.allChecked()).toBe(false);
    expect(host.querySelector<HTMLInputElement>('#invite-branch-b-1')?.disabled).toBe(false);
    expect(host.querySelector('#invite-team-link')).not.toBeNull();
    expect(host.querySelector('#invite-branch-b-3')).toBeNull();
    form.onBranchChange('b-1', true);
    form.onBranchChange('b-2', true);
    expect(form.allChecked()).toBe(true);
    form.onBranchChange('b-1', false);
    expect(form.allChecked()).toBe(false);
    form.onAllBranchesChange(true);
    expect(form.isBranchChecked('b-2')).toBe(true);
    form.onAllBranchesChange(false);
    expect(form.isBranchChecked('b-2')).toBe(false);

    // Invalid submit sends nothing and shows the BR-08 messages.
    button('Send invitation')!.click();
    await settle();
    httpTesting.expectNone((r) => r.method === 'POST');
    const summary = host.querySelector('#user-drawer-error-summary');
    expect(summary?.textContent).toContain('Email address: Enter a valid email address.');
    expect(summary?.querySelector('a')).toBeNull();
    expect(host.querySelector('#invite-email-error')?.textContent?.trim()).toBe(
      'Enter a valid email address.',
    );
    expect(host.querySelector('#invite-first-name-error')?.textContent?.trim()).toBe(
      'Enter a first name.',
    );
    expect(host.querySelector('#invite-branches-error')?.textContent?.trim()).toBe(
      'Select at least one branch.',
    );

    form.onFieldInput('email', ' New.User@Acme.com ');
    form.onFieldInput('firstName', 'New');
    form.onFieldInput('lastName', 'User');
    form.onBranchChange('b-1', true);
    const submit = async (): Promise<TestRequest> => {
      button('Send invitation')!.click();
      await settle();
      return call('POST', 'users/invitations');
    };

    let request = await submit();
    expect(request.request.body).toEqual({
      roleCode: 'dispatcher',
      isAllBranches: false,
      branchIds: ['b-1'],
      email: 'new.user@acme.com',
      firstName: 'New',
      lastName: 'User',
      linkTeamProfile: false,
      expiresInDays: 7,
    });
    expect(form.submitting()).toBe(true);
    request.flush(
      { status: 409, errors: { email: ['This person already has access.'] } },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(host.querySelector('#invite-email-error')?.textContent?.trim()).toBe(
      'This person already has access.',
    );
    expect(form.submitting()).toBe(false);

    form.onFieldInput('email', 'other@acme.com');
    request = await submit();
    request.flush(null, { status: 502, statusText: 'Bad Gateway' });
    await settle();
    expect(text()).toContain("We couldn't send the invitation. Try again.");
    expect(page.drawerOpen()).toBe(true);
    expect(host.querySelector<HTMLInputElement>('#invite-email')?.value).toBe('other@acme.com');

    request = await submit();
    request.flush(
      { ...OLIVIA, email: 'other@acme.com', isExpired: false },
      { status: 201, statusText: 'Created' },
    );
    await settle();
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Invitation sent to other@acme.com',
    });
    expect(page.drawerOpen()).toBe(false);
    flushList();
    call('GET', 'users/summary').flush(SUMMARY);
  });

  it('confirms and runs row actions, disables protected items and edits access with the reduction confirmation (FR-09, FR-10, FR-12, AC-10, AC-12, AC-14)', async () => {
    await setup('owner');
    const messages = vi.spyOn(inject(MessageService), 'add');
    const confirmations: Confirmation[] = [];
    vi.spyOn(inject(ConfirmationService), 'confirm').mockImplementation((confirmation) => {
      confirmations.push(confirmation);
      return undefined as never;
    });
    const items = (r: UserRow): MenuItem[] => page.buildMenu(r);
    const refresh = (): void => {
      flushList();
      call('GET', 'users/summary').flush(SUMMARY);
    };

    // BR-12 menus by status; last Owner / self items are disabled with tooltips.
    expect(items(ALEX)[0]).toMatchObject({
      label: 'Suspend access',
      disabled: true,
      tooltip: "You can't suspend your own access.",
    });
    expect(items({ ...ALEX, isCurrentUser: false })[0]).toMatchObject({
      disabled: true,
      tooltip: "The last Owner can't be suspended or downgraded.",
    });
    expect(items(DANIEL).map((i) => [i.label, i.disabled])).toEqual([['Suspend access', false]]);
    expect(items(SAM).map((i) => i.label)).toEqual(['Reactivate']);
    expect(items(OLIVIA).map((i) => [i.label, i.disabled])).toEqual([
      ['Resend invitation', false],
      ['Revoke invitation', false],
    ]);

    // Suspend: confirmation focuses the safe action; Cancel sends nothing; confirm sends and refreshes.
    items(DANIEL)[0].command!({});
    expect(confirmations[0]).toMatchObject({
      message:
        "Suspend Daniel Kim? They won't be able to sign in. Assigned work and audit history are preserved.",
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Suspend', severity: 'danger' },
    });
    httpTesting.expectNone((r) => r.method === 'POST');
    confirmations[0].accept!();
    call('POST', 'users/m-3/suspend').flush(null, { status: 204, statusText: 'No Content' });
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Access suspended for Daniel Kim',
    });
    refresh();

    // Reactivate and Resend need no confirmation; Revoke confirms.
    items(SAM)[0].command!({});
    call('POST', 'users/m-4/reactivate').flush(null, { status: 204, statusText: 'No Content' });
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Access reactivated for Sam Lee',
    });
    refresh();
    items(OLIVIA)[0].command!({});
    call('POST', 'users/invitations/i-1/resend').flush(OLIVIA);
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Invitation resent to olivia@acme.com',
    });
    refresh();
    items(OLIVIA)[1].command!({});
    expect(confirmations[1].message).toBe(
      'Revoke the invitation for olivia@acme.com? The link will stop working.',
    );
    confirmations[1].accept!();
    call('POST', 'users/invitations/i-1/revoke').flush(null, {
      status: 409,
      statusText: 'Conflict',
    });
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: 'This invitation is no longer pending.',
    });
    refresh();
    items(DANIEL)[0].command!({});
    confirmations[2].accept!();
    call('POST', 'users/m-3/suspend').flush(null, { status: 404, statusText: 'Not Found' });
    expect(messages).toHaveBeenCalledWith({
      severity: 'error',
      summary: 'This user is no longer available.',
    });
    refresh();

    // Edit access: read-only identity, no team-link or expiry, reduction asks first.
    await settle();
    Array.from(host.querySelectorAll<HTMLButtonElement>('.user-table__edit'))[1].click();
    await settle();
    const form = drawer();
    expect(host.querySelector('.user-drawer__title')?.textContent?.trim()).toBe('Edit access');
    expect(host.querySelector('.user-drawer__identity')?.textContent).toContain('daniel@acme.com');
    expect(host.querySelector('#invite-first-name')).toBeNull();
    expect(host.querySelector('#invite-team-link')).toBeNull();
    expect(host.querySelector('#invite-expiry')).toBeNull();
    expect(button('Save access')).toBeDefined();

    form.onRoleChange('viewer');
    button('Save access')!.click();
    await settle();
    expect(confirmations[3].message).toBe(
      'Change access for Daniel Kim? Role: Dispatcher → Viewer. Branches: Austin Central → Austin Central.',
    );
    httpTesting.expectNone((r) => r.method === 'PUT');
    confirmations[3].accept!();
    const put = call('PUT', 'users/m-3/access');
    expect(put.request.body).toEqual({
      roleCode: 'viewer',
      isAllBranches: false,
      branchIds: ['b-1'],
    });
    put.flush({ ...DANIEL, roleCode: 'viewer', roleName: 'Viewer' });
    await settle();
    expect(messages).toHaveBeenCalledWith({
      severity: 'success',
      summary: 'Access updated for Daniel Kim',
    });
    refresh();

    // Invitations route to their own endpoint; an increase needs no confirmation; the last Owner stays Owner.
    page.openEdit(OLIVIA);
    await settle();
    drawer().onRoleChange('operations_manager');
    button('Save access')!.click();
    await settle();
    expect(confirmations).toHaveLength(4);
    call('PUT', 'users/invitations/i-1/access').flush({ ...OLIVIA });
    refresh();
    page.openEdit(ALEX);
    await settle();
    expect(
      drawer()
        .roleOptions()
        .filter((option) => option.disabled)
        .map((option) => option.code),
    ).toEqual(['operations_manager', 'dispatcher', 'technician', 'accounting', 'viewer']);
  });

  it('asks before discarding a dirty drawer on close and route leave (FR-09, AC-12)', async () => {
    await setup('owner');
    const confirmations: Confirmation[] = [];
    vi.spyOn(inject(ConfirmationService), 'confirm').mockImplementation((confirmation) => {
      confirmations.push(confirmation);
      return undefined as never;
    });

    expect(page.canLeave()).toBe(true);
    button('Invite user')!.click();
    await settle();
    expect(page.canLeave()).toBe(true);

    drawer().onFieldInput('firstName', 'Dana');
    const leave: boolean[] = [];
    (page.canLeave() as Observable<boolean>).subscribe((value) => leave.push(value));
    expect(confirmations[0]).toMatchObject({
      header: 'Discard unsaved changes?',
      defaultFocus: 'reject',
    });
    confirmations[0].reject!();
    expect(leave).toEqual([false]);

    drawer().requestClose();
    expect(confirmations).toHaveLength(2);
    expect(page.drawerOpen()).toBe(true);
    confirmations[1].accept!();
    expect(page.drawerOpen()).toBe(false);
  });
});
