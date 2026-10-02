import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ServiceRequestForm } from '../../models/service-request.model';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { LOAD_ERROR_MESSAGE, UNAVAILABLE_MESSAGE } from '../../service-request.messages';
import { ServiceRequest } from './service-request';

@Component({ template: '' })
class SignInStub {}

const FORM_URL = 'http://api.test/public/organizations/acme/service-request-form';
const CONFIG: ServiceRequestForm = {
  organizationName: 'Acme Plumbing',
  phone: '(512) 555-0199',
  website: null,
  requestPrefix: 'REQ',
  timezone: 'America/Chicago',
  categories: [{ id: 'c1', name: 'Plumbing', services: [{ id: 's1', name: 'Leak repair' }] }],
};

describe('ServiceRequest page', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let host: HTMLElement;

  const text = () => host.textContent?.replace(/\s+/g, ' ') ?? '';

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideRouter([
          { path: 'request/:slug', component: ServiceRequest },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    http = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl('/request/acme', ServiceRequest);
    host = harness.routeNativeElement as HTMLElement;
  });

  afterEach(() => http.verify());

  it('shows the unavailable page without a form on 404 (AC-02)', async () => {
    http
      .expectOne(FORM_URL)
      .flush({ title: 'Not found' }, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();

    expect(text()).toContain(UNAVAILABLE_MESSAGE);
    expect(host.querySelector('form')).toBeNull();
    expect(host.querySelector('app-contact-step')).toBeNull();
  });

  it('shows a configuration error with Retry on 500 and recovers (FR-15)', async () => {
    http.expectOne(FORM_URL).flush(null, { status: 500, statusText: 'Server Error' });
    await harness.fixture.whenStable();
    expect(text()).toContain(LOAD_ERROR_MESSAGE);
    expect(host.querySelector('form')).toBeNull();

    host.querySelector<HTMLButtonElement>('button')?.click();
    http.expectOne(FORM_URL).flush(CONFIG);
    await harness.fixture.whenStable();

    expect(host.querySelector('app-contact-step form')).not.toBeNull();
  });

  it('header shows organization, a Sign in link and non-interactive labels (AC-25)', async () => {
    http.expectOne(FORM_URL).flush(CONFIG);
    await harness.fixture.whenStable();

    expect(host.querySelector('.topbar__org')?.textContent).toBe('Acme Plumbing');
    expect(host.querySelector('.topbar__phone')?.textContent).toBe('(512) 555-0199');
    const links = Array.from(host.querySelectorAll<HTMLAnchorElement>('.topbar a'));
    expect(links.map((a) => [a.textContent?.trim(), a.getAttribute('href')])).toEqual([
      ['Sign in', '/auth/sign-in'],
    ]);
    const labels = Array.from(host.querySelectorAll<HTMLElement>('.topbar__labels span'));
    expect(labels.map((l) => l.textContent)).toEqual(['Services', 'How it works', 'Help']);
    expect(labels.every((l) => !l.hasAttribute('tabindex'))).toBe(true);
    expect(host.querySelectorAll('.topbar__labels a, .topbar__labels button')).toHaveLength(0);
  });

  it('contact step: Back to home restarts or leaves, Privacy is plain text (BR-22)', async () => {
    http.expectOne(FORM_URL).flush(CONFIG);
    await harness.fixture.whenStable();

    const back = host.querySelector<HTMLButtonElement>('[data-sr-back]');
    expect(back?.tagName).toBe('BUTTON');
    expect(back?.textContent).toContain('Back to home');
    const privacy = host.querySelector<HTMLElement>('.panel__privacy');
    expect(privacy?.textContent).toBe('Privacy');
    expect(privacy?.hasAttribute('tabindex')).toBe(false);
    expect(host.querySelectorAll('.panel a, .panel__privacy button')).toHaveLength(0);
    expect(host.querySelector('.stepper__item[aria-current="step"]')).not.toBeNull();
  });

  it('contact step: Back to home links to the organization website when set', async () => {
    http.expectOne(FORM_URL).flush({ ...CONFIG, website: 'acme.example' });
    await harness.fixture.whenStable();

    const back = host.querySelector<HTMLAnchorElement>('a[data-sr-back]');
    expect(back?.getAttribute('href')).toBe('https://acme.example');
  });

  it('renders the Property step completely on arrival, without an extra interaction', async () => {
    http.expectOne(FORM_URL).flush(CONFIG);
    await harness.fixture.whenStable();
    const store = harness.routeDebugElement!.injector.get(ServiceRequestWizardStore);
    store.patchContact({ firstName: 'Sofia', lastName: 'Martinez', email: 's@example.com' });
    store.patchContact({ phone: '(512) 555-0147' });

    host.querySelector<HTMLButtonElement>('[data-sr-continue]')?.click();
    // A render error (e.g. NG01352 from a nameless ngModel inside the form) would be rethrown here.
    await harness.fixture.whenStable();

    const step = host.querySelector('app-property-step');
    const tiles = Array.from(step?.querySelectorAll('.sr-tile') ?? []).map((t) =>
      t.textContent?.trim(),
    );
    expect(tiles).toEqual(['Home', 'Business']);
    expect(step?.querySelector('p-select .p-select-label')).not.toBeNull();
    expect(host.querySelector('.panel__section')?.textContent).toContain('Sofia Martinez');
  });

  it('stepper: only completed steps are buttons and navigate back with data intact', async () => {
    http.expectOne(FORM_URL).flush(CONFIG);
    await harness.fixture.whenStable();
    const store = harness.routeDebugElement!.injector.get(ServiceRequestWizardStore);
    store.patchContact({ firstName: 'Sofia', lastName: 'Martinez', email: 's@example.com' });
    store.patchContact({ phone: '(512) 555-0147' });
    host.querySelector<HTMLButtonElement>('[data-sr-continue]')?.click();
    await harness.fixture.whenStable();

    const links = () => Array.from(host.querySelectorAll<HTMLButtonElement>('.stepper__link'));
    expect(links().map((b) => b.getAttribute('aria-label'))).toEqual(['Go to Contact, completed']);
    expect(host.querySelector('.stepper__item[aria-current="step"] button')).toBeNull();

    links()[0].click();
    await harness.fixture.whenStable();
    expect(store.step()).toBe('contact');
    expect(store.data().contact.firstName).toBe('Sofia');
    expect(links().map((b) => b.getAttribute('aria-label'))).toEqual([]);
  });
});
