import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../../../core/config/api.config';
import { errorInterceptor } from '../../../core/interceptors/error.interceptor';
import { ServiceRequestForm, ServiceRequestPayload } from '../models/service-request.model';
import {
  ATTACHMENT_SIZE_MESSAGE,
  CONSENT_REQUIRED_MESSAGE,
  RATE_LIMITED_MESSAGE,
  SERVER_FIELD_MESSAGE,
  SUBMIT_ERROR_MESSAGE,
} from '../service-request.messages';
import { ServiceRequestWizardStore } from './service-request-wizard.store';

const API = 'http://api.test';
const URL = `${API}/public/organizations/acme/service-requests`;
const CONFIG: ServiceRequestForm = {
  organizationName: 'Acme',
  phone: '(512) 555-0199',
  website: null,
  requestPrefix: 'REQ',
  timezone: 'America/Chicago',
  categories: [{ id: 'c1', name: 'Plumbing', services: [{ id: 's1', name: 'Leak repair' }] }],
};

describe('ServiceRequestWizardStore', () => {
  let store: ServiceRequestWizardStore;
  let http: HttpTestingController;

  function fill(): void {
    store.patchContact({
      firstName: 'Sofia',
      lastName: 'Martinez',
      email: 'sofia@example.com',
      phone: '(512) 555-0147',
    });
    store.patchProperty({
      addressLine1: '742 Maple Ave',
      city: 'Austin',
      state: 'TX',
      postalCode: '78704',
    });
    store.patchService({ categoryId: 'c1', serviceId: 's1', description: 'Leaking sink' });
  }

  function reachReview(): void {
    fill();
    for (let i = 0; i < 4; i++) {
      expect(store.next()).toBe(true);
    }
    expect(store.step()).toBe('review');
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ServiceRequestWizardStore,
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(ServiceRequestWizardStore);
    http = TestBed.inject(HttpTestingController);
    store.configure('acme', CONFIG);
  });

  afterEach(() => http.verify());

  it('blocks Continue on invalid steps and keeps data across Back and Edit (AC-03, AC-04)', () => {
    expect(store.next()).toBe(false);
    expect(store.step()).toBe('contact');
    expect(store.error('contact.firstName')).not.toBeNull();
    expect(store.focusRequest()?.target).toBe('contact.firstName');

    fill();
    expect(store.next()).toBe(true);
    expect(store.step()).toBe('property');
    expect(store.error('contact.firstName')).toBeNull();

    store.back();
    expect(store.step()).toBe('contact');
    expect(store.data().contact.email).toBe('sofia@example.com');

    expect(store.next()).toBe(true);
    store.edit('property');
    expect(store.data().property.addressLine1).toBe('742 Maple Ave');
    store.edit('availability'); // not completed yet: ignored
    expect(store.step()).toBe('property');

    store.patchContact({ firstName: 'Sofía' });
    expect(store.summary()[0].lines[0]).toBe('Sofía Martinez');
    expect(store.summary()[2].lines).toEqual([]); // "Not added yet"
  });

  it('requires consent, sends once, shows confirmation and resets (AC-05, AC-08, AC-22)', async () => {
    reachReview();
    store.patchService({ notSure: true });
    expect(store.data().service.serviceId).toBe('');
    store.files.set([new File(['x'], 'sink.jpg', { type: 'image/jpeg' })]);
    store.website.set('');

    store.submit();
    expect(store.error('consent')).toBe(CONSENT_REQUIRED_MESSAGE);
    expect(store.submitting()).toBe(false);

    store.setConsent(true);
    store.submit();
    store.submit(); // in flight: ignored
    const request = http.expectOne(URL);
    expect(store.submitting()).toBe(true);

    const body = request.request.body as FormData;
    const payload = JSON.parse(await (body.get('request') as Blob).text()) as ServiceRequestPayload;
    expect((body.get('request') as Blob).type).toBe('application/json');
    expect(payload.consent).toBe(true);
    expect(payload.website).toBe('');
    expect(payload.service.serviceId).toBeUndefined();
    expect(payload.availability.preferredDate).toBeUndefined();
    expect(payload.contact.phone).toBe('(512) 555-0147');
    expect((body.getAll('attachments')[0] as File).name).toBe('sink.jpg');

    request.flush({ requestNumber: 'REQ-1048' }, { status: 201, statusText: 'Created' });
    expect(store.result()).toMatchObject({
      requestNumber: 'REQ-1048',
      firstName: 'Sofia',
      email: 'sofia@example.com',
    });
    // The confirmation keeps a snapshot of what was submitted after the data is cleared.
    expect(store.result()?.submitted).toEqual({
      contact: ['Sofia Martinez', 'sofia@example.com'],
      property: ['742 Maple Ave', 'Austin, TX 78704'],
      service: 'Plumbing · Standard',
      preferredTime: 'As soon as possible · Morning',
    });
    expect(store.data().contact.email).toBe('');

    store.reset();
    expect(store.result()).toBeNull();
    expect(store.step()).toBe('contact');
    expect(store.files()).toEqual([]);
  });

  it.each<
    [string, number, object | null, { step: string; field?: [string, string]; banner?: string }]
  >([
    [
      'validation → earliest failing step with fixed messages',
      400,
      {
        title: 'Backend text',
        errors: { 'service.description': ['x'], 'contact.email': ['Backend text'] },
      },
      { step: 'contact', field: ['contact.email', SERVER_FIELD_MESSAGE] },
    ],
    [
      'generic bad request (honeypot/malformed)',
      400,
      { title: 'Backend text' },
      { step: 'review', banner: SUBMIT_ERROR_MESSAGE },
    ],
    [
      '413 → attachment size on Service details',
      413,
      null,
      { step: 'service', field: ['attachments', ATTACHMENT_SIZE_MESSAGE] },
    ],
    ['429 → rate limit message', 429, null, { step: 'review', banner: RATE_LIMITED_MESSAGE }],
    [
      '500 → generic submit error',
      500,
      { title: 'Backend text' },
      { step: 'review', banner: SUBMIT_ERROR_MESSAGE },
    ],
    ['network error', 0, null, { step: 'review', banner: SUBMIT_ERROR_MESSAGE }],
  ])('maps %s and retains data (AC-20, AC-23)', (_name, status, body, expected) => {
    reachReview();
    store.setConsent(true);
    store.submit();
    const request = http.expectOne(URL);
    if (status === 0) {
      request.error(new ProgressEvent('error'));
    } else {
      request.flush(body, { status, statusText: 'Error' });
    }

    expect(store.submitting()).toBe(false);
    expect(store.step()).toBe(expected.step);
    expect(store.submitError()).toBe(expected.banner ?? null);
    if (expected.field !== undefined) {
      expect(store.error(expected.field[0])).toBe(expected.field[1]);
    }
    expect(store.result()).toBeNull();
    expect(store.data().contact.firstName).toBe('Sofia');
    expect(JSON.stringify([store.submitError(), store.stepErrors()])).not.toContain('Backend text');
  });
});
