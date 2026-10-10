import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { PortalSessionService } from '../../../../core/services/portal-session.service';
import { Dashboard, DashboardProperty, PortalAppointment } from '../../models/portal.model';
import { greeting, rescheduleRange, toDateOnly } from '../../utils/portal-format';
import { PortalHome } from './home';

const API = 'http://api.test';
const URL = `${API}/portal/dashboard`;

const PROPERTIES: readonly DashboardProperty[] = [
  {
    id: 'p-2',
    name: 'Cabin',
    addressLine1: '9 Pine Rd',
    addressLine2: null,
    city: 'Dripping Springs',
    state: 'TX',
    postalCode: '78620',
    isPrimary: false,
    lastServiceOn: null,
  },
  {
    id: 'p-1',
    name: 'Home',
    addressLine1: '742 Maple Ave',
    addressLine2: null,
    city: 'Austin',
    state: 'TX',
    postalCode: '78704',
    isPrimary: true,
    lastServiceOn: '2026-09-20',
  },
];

const APPOINTMENT: PortalAppointment = {
  visitId: 'v-1',
  workOrderId: 'w-1',
  workOrderNumber: 'WO-1064',
  title: 'Annual HVAC maintenance',
  date: '2026-10-28',
  startTime: '09:00',
  endTime: '11:00',
  arrivalWindow: { start: '09:00', end: '11:00' },
  technician: { fullName: 'Carlos Rivera', firstName: 'Carlos' },
  property: { name: 'Home', addressLine1: '742 Maple Ave' },
  status: 'assigned',
  progressStep: 0,
  canRequestReschedule: true,
  rescheduleRequestedOn: null,
  reportAvailable: false,
};

const EMPTY: Dashboard = {
  organization: { name: 'Northstar', phone: null, canReceiveMessages: false },
  properties: [],
  selectedPropertyId: null,
  actionQuote: null,
  paymentDue: null,
  upcomingAppointment: null,
  activeRequests: [],
  updates: { items: [], unreadCount: 0 },
  recentActivity: [],
};

const FULL: Dashboard = {
  ...EMPTY,
  organization: { name: 'Northstar', phone: '(512) 555-0199', canReceiveMessages: true },
  properties: PROPERTIES,
  actionQuote: {
    id: 'q-1',
    displayNumber: 'Q-1052',
    scope: 'Bathroom faucet replacement',
    total: 289.4,
    currency: 'USD',
    validUntil: '2026-10-27',
    status: 'sent',
    moreCount: 1,
  },
  paymentDue: {
    id: 'i-1',
    displayNumber: 'INV-1051',
    title: 'Water heater service',
    balanceDue: 149,
    currency: 'USD',
    dueDate: '2026-10-03',
    isOverdue: true,
    moreCount: 0,
  },
  upcomingAppointment: APPOINTMENT,
  activeRequests: [
    {
      id: 'r-1',
      displayNumber: 'REQ-1091',
      title: 'Bathroom faucet replacement',
      propertyName: 'Home',
      status: 'quoted',
      statusLabel: 'Quote ready',
      submittedOn: '2026-09-19',
    },
  ],
  updates: {
    items: [
      {
        type: 'quote_sent',
        title: 'Quote Q-1052 ready',
        subtitle: 'Bathroom faucet replacement',
        occurredAt: new Date().toISOString(),
        isUnread: true,
        target: { kind: 'quote', id: 'q-1' },
      },
    ],
    unreadCount: 1,
  },
  recentActivity: [
    {
      workOrderId: 'w-9',
      title: 'Kitchen sink leak repair',
      completedOn: '2026-09-20',
      reference: 'WO-1048',
      amount: 357.28,
      currency: 'USD',
      status: 'paid',
      invoiceId: 'i-9',
      receiptPaymentId: 'pay-9',
    },
  ],
};

describe('PortalHome', () => {
  let fixture: ComponentFixture<PortalHome>;
  let httpTesting: HttpTestingController;

  function setup(): void {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(PortalSessionService).setSession({
      user: { firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
      account: {
        contactId: 'c-1',
        customerName: 'Sofia Martinez',
        organizationName: 'Northstar',
        hasLogo: false,
      },
      accounts: [{ contactId: 'c-1', customerName: 'Sofia Martinez', organizationName: 'Northstar' }],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PortalHome);
    fixture.detectChanges();
  }

  const host = () => fixture.nativeElement as HTMLElement;
  const text = () => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const settle = async (): Promise<void> => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  /** The dashboard request with the given `propertyId` (`null`: the parameter is absent). */
  const dashboardRequest = (propertyId: string | null): TestRequest =>
    httpTesting.expectOne(
      (request) =>
        request.url === URL &&
        request.params.keys().length === (propertyId === null ? 0 : 1) &&
        request.params.get('propertyId') === propertyId,
    );
  async function respond(request: TestRequest, body: object | null, status = 200): Promise<void> {
    request.flush(body, { status, statusText: status === 200 ? 'OK' : 'Error' });
    await settle();
  }
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  async function click(label: string): Promise<void> {
    button(label)?.click();
    await settle();
  }
  async function type(selector: string, value: string): Promise<void> {
    const field = document.body.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  }

  afterEach(() => httpTesting.verify());

  it('renders the cards, the empty states and the error with Try again (AC-15, AC-16)', async () => {
    // A new customer with no data and no properties: one request, no scoped reload.
    setup();
    expect(host().querySelectorAll('app-portal-card-skeleton').length).toBeGreaterThan(0);
    await respond(dashboardRequest(null), EMPTY);
    for (const empty of [
      'No quotes need your attention.',
      "You're all paid up.",
      'No upcoming appointments.',
      'No active requests.',
      'No updates yet.',
      'No properties yet.',
      'No completed services yet.',
    ]) {
      expect(text()).toContain(empty);
    }
    expect(host().querySelector('app-portal-property-filter')).toBeNull();
    expect(text()).not.toContain('Call ');
    expect(text()).not.toContain('Send a message');
    fixture.destroy();
    TestBed.resetTestingModule();

    // Full data (all properties offered first with two properties).
    setup();
    await respond(dashboardRequest(null), FULL);
    await respond(dashboardRequest('p-1'), { ...FULL, selectedPropertyId: 'p-1' });
    for (const expected of [
      'Quote ready',
      'Q-1052 · Total $289.40',
      'Approving this quote does not confirm an appointment.',
      '+1 more quotes',
      'Invoice INV-1051',
      'Overdue · Due Oct 3, 2026',
      'Annual HVAC maintenance',
      'Wednesday, Oct 28, 2026',
      "We'll let you know when Carlos is on the way.",
      'REQ-1091',
      'Quote ready',
      'Quote Q-1052 ready',
      'Kitchen sink leak repair',
      'Completed + Paid',
      'Call (512) 555-0199',
      'Send a message',
      'Last service: Sep 20, 2026',
    ]) {
      expect(text()).toContain(expected);
    }
    expect(host().querySelector('a[href^="tel:"]')).not.toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();

    // A failing dashboard shows the alert; Try again reloads.
    setup();
    await respond(dashboardRequest(null), null, 500);
    expect(text()).toContain("We couldn't load your dashboard.");
    await click('Try again');
    await respond(dashboardRequest(null), EMPTY);
    expect(text()).not.toContain("We couldn't load your dashboard.");
  });

  it('sends propertyId from the selector and resets to the default when it returns 404 (BR-18, AS-04)', async () => {
    setup();
    // First load: no parameter; the default (primary) is applied once and the dashboard reloaded.
    await respond(dashboardRequest(null), FULL);
    await respond(dashboardRequest('p-1'), { ...FULL, selectedPropertyId: 'p-1' });
    expect(fixture.componentInstance.selected()).toBe('p-1');
    expect(fixture.componentInstance.showAll()).toBe(true);

    // Choosing another property sends its id; All sends none and is not overridden by the default.
    fixture.componentInstance.select('p-2');
    await respond(dashboardRequest('p-2'), { ...FULL, selectedPropertyId: 'p-2' });
    expect(fixture.componentInstance.selectedProperty()?.id).toBe('p-2');
    fixture.componentInstance.select(null);
    await respond(dashboardRequest(null), FULL);
    expect(fixture.componentInstance.selected()).toBeNull();
    expect(fixture.componentInstance.selectedProperty()?.isPrimary).toBe(true);

    // The selected property is gone: back to the default and a reload.
    fixture.componentInstance.select('p-2');
    await respond(dashboardRequest('p-2'), null, 404);
    await respond(dashboardRequest(null), FULL);
    await respond(dashboardRequest('p-1'), { ...FULL, selectedPropertyId: 'p-1' });
    expect(fixture.componentInstance.selected()).toBe('p-1');
    expect(text()).not.toContain("We couldn't load your dashboard.");
  });

  it('reschedule and message dialogs submit once, map 409 and patch the cards in place (BR-32, BR-35)', async () => {
    setup();
    await respond(dashboardRequest(null), { ...FULL, properties: [PROPERTIES[1]] });
    await respond(dashboardRequest('p-1'), { ...FULL, properties: [PROPERTIES[1]] });

    // Reschedule: a double click sends one request; 409 shows its message and keeps the dialog.
    await click('Request reschedule');
    await type('#portal-reschedule-date', toDateOnly(rescheduleRange().min));
    await type('#portal-reschedule-reason', 'I will be travelling');
    button('Send request')!.click();
    button('Send request')!.click();
    const reschedule = httpTesting.expectOne(`${API}/portal/appointments/v-1/reschedule-requests`);
    expect(reschedule.request.body).toEqual({
      preferredDate: toDateOnly(rescheduleRange().min),
      timeWindow: 'any',
      reason: 'I will be travelling',
    });
    await respond(reschedule, { code: 'reschedule_already_requested' }, 409);
    // (the error interceptor maps the body code; the dialog keeps the form open)
    expect(text()).toContain('Request a reschedule');
    await click('Send request');
    await respond(
      httpTesting.expectOne(`${API}/portal/appointments/v-1/reschedule-requests`),
      { requestedOn: '2026-10-10' },
      201,
    );
    expect(text()).toContain('Reschedule requested · Oct 10, 2026');
    expect(button('Request reschedule')).toBeUndefined();

    // Message: one submission; 409 messaging_unavailable hides Send a message.
    await click('Send a message');
    await type('#portal-message-text', 'Hello team');
    button('Send message')!.click();
    button('Send message')!.click();
    const message = httpTesting.expectOne(`${API}/portal/messages`);
    expect(message.request.body).toEqual({ message: 'Hello team' });
    await respond(message, { code: 'messaging_unavailable' }, 409);
    expect(button('Send a message')).toBeUndefined();
  });
});

describe('portal greeting', () => {
  it('greets by the browser local time (BR-17)', () => {
    expect(greeting(new Date(2026, 9, 10, 0, 0), 'Sofia')).toBe('Good morning, Sofia');
    expect(greeting(new Date(2026, 9, 10, 11, 59), 'Sofia')).toBe('Good morning, Sofia');
    expect(greeting(new Date(2026, 9, 10, 12, 0), 'Sofia')).toBe('Good afternoon, Sofia');
    expect(greeting(new Date(2026, 9, 10, 17, 59), 'Sofia')).toBe('Good afternoon, Sofia');
    expect(greeting(new Date(2026, 9, 10, 18, 0), 'Sofia')).toBe('Good evening, Sofia');
  });
});
