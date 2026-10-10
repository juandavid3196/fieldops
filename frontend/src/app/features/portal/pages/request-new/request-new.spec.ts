import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ServiceRequestForm } from '../../../service-request/models/service-request.model';
import { ServiceRequestWizardStore } from '../../../service-request/services/service-request-wizard.store';
import { PortalRequestNew } from './request-new';

const API = 'http://api.test';

const FORM: ServiceRequestForm = {
  organizationName: 'Northstar',
  phone: '(512) 555-0199',
  website: null,
  requestPrefix: 'REQ',
  timezone: 'America/Chicago',
  categories: [{ id: 'cat-1', name: 'Plumbing', services: [{ id: 'srv-1', name: 'Faucet repair' }] }],
};

const property = (id: string, name: string) => ({
  id,
  name,
  addressLine1: `${name} street`,
  addressLine2: null,
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78704',
  countryCode: 'US',
  accessInstructions: null,
  isPrimary: id === 'p-1',
  lastServiceOn: null,
  updatedAt: '2026-10-01T10:00:00Z',
});

describe('PortalRequestNew', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  async function setup(
    form: ServiceRequestForm = FORM,
    url = '/portal/requests/new?propertyId=p-1',
  ): Promise<ServiceRequestWizardStore> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'portal/requests/new', component: PortalRequestNew }]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl(url, PortalRequestNew);
    httpTesting.expectOne(`${API}/portal/service-request-form`).flush(form);
    httpTesting
      .expectOne(`${API}/portal/properties`)
      .flush([property('p-1', 'Home'), property('p-2', 'Cabin')]);
    await settle();
    return harness.routeDebugElement!.injector.get(ServiceRequestWizardStore);
  }

  const settle = async (): Promise<void> => {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  };
  const text = () => (harness.fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ').trim();

  /** The multipart `request` part of the submission, parsed. */
  async function submitted(request: TestRequest): Promise<Record<string, unknown>> {
    const body = request.request.body as FormData;
    expect(request.request.url).toBe(`${API}/portal/service-requests`);
    expect(Array.from(body.keys())).toEqual(['request']);
    return JSON.parse(await (body.get('request') as Blob).text()) as Record<string, unknown>;
  }

  function fillRequest(store: ServiceRequestWizardStore): void {
    store.patchService({ categoryId: 'cat-1', serviceId: 'srv-1', description: 'Leaking faucet' });
    store.setConsent(true);
  }

  afterEach(() => httpTesting.verify());

  it('skips the Contact step and submits an existing property id with no contact', async () => {
    const store = await setup();
    // The wizard starts at Property with the selected property chosen; there is no Contact step.
    expect(store.step()).toBe('property');
    expect(store.propertyChoice()).toBe('p-1');
    expect(text()).not.toContain('Contact');
    expect(text()).toContain('Add a new property');
    expect(text()).toContain('Cabin — Cabin street');

    fillRequest(store);
    expect(store.next()).toBe(true);
    expect(store.step()).toBe('service');
    store.next();
    store.next();
    expect(store.step()).toBe('review');
    store.submit();
    const request = httpTesting.expectOne(`${API}/portal/service-requests`);
    const payload = await submitted(request);
    expect(payload['property']).toEqual({ propertyId: 'p-1' });
    expect(payload).not.toHaveProperty('contact');
    expect(payload['consent']).toBe(true);
    expect(payload['website']).toBe('');

    request.flush({ requestId: 'r-77', requestNumber: 'REQ-1077' }, { status: 201, statusText: 'Created' });
    await settle();
    expect(text()).toContain('Request received');
    expect(text()).toContain('REQ-1077');
    const view = Array.from(harness.fixture.nativeElement.querySelectorAll('a')).find(
      (anchor) => (anchor as HTMLElement).textContent?.trim() === 'View request',
    ) as HTMLAnchorElement;
    expect(view.getAttribute('href')).toBe('/portal/requests/r-77');
  });

  it('submits a new property, maps the 400 property.propertyId error and handles an unavailable form', async () => {
    const store = await setup(FORM, '/portal/requests/new');
    expect(store.propertyChoice()).toBe('');

    // Continuing without choosing a property is blocked with the portal message.
    expect(store.next()).toBe(false);
    expect(store.error('property.propertyId')).toBe('Select one of your properties.');

    store.choosePropertyOption('new');
    store.patchProperty({
      addressLine1: '1 New St',
      city: 'Austin',
      state: 'TX',
      postalCode: '78701',
    });
    fillRequest(store);
    store.next();
    store.next();
    store.next();
    store.submit();
    const first = httpTesting.expectOne(`${API}/portal/service-requests`);
    const payload = await submitted(first);
    expect(payload['property']).toEqual({
      newProperty: {
        propertyType: 'home',
        addressLine1: '1 New St',
        addressLine2: '',
        city: 'Austin',
        state: 'TX',
        postalCode: '78701',
        accessInstructions: '',
      },
    });

    // The server rejects a foreign property id: the field error shows on the Property step.
    first.flush(
      { status: 400, errors: { 'property.propertyId': ['Select one of your properties.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(store.step()).toBe('property');
    expect(store.error('property.propertyId')).toBe('Select one of your properties.');
    expect(store.submitting()).toBe(false);
    httpTesting.verify();
    TestBed.resetTestingModule();

    // No requestable service: the unavailable message with the phone, and no form.
    await setup({ ...FORM, categories: [] });
    expect(text()).toContain("Online requests aren't available right now. Call (512) 555-0199.");
    expect(harness.fixture.nativeElement.querySelector('form')).toBeNull();
  });
});
