import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { TechnicianVisitDetail } from '../../models/technician-visits.model';
import { VisitDetail } from './visit-detail';

const API = 'http://api.test';
const URL = `${API}/technician/visits/v-1`;

const DETAIL: TechnicianVisitDetail = {
  visitId: 'v-1',
  visitNumber: 2,
  workOrderId: 'wo-1',
  displayNumber: 'WO-3091',
  title: 'Kitchen sink leak repair',
  status: 'in_progress',
  start: '2026-10-07T09:30:00+13:00',
  end: '2026-10-07T11:30:00+13:00',
  arrivalWindowStart: null,
  arrivalWindowEnd: null,
  priority: 1,
  serviceCategory: 'Plumbing',
  customerName: 'Sofia Martinez',
  phone: '5125550100',
  address: {
    line1: '1842 Oak Street',
    line2: null,
    city: 'Austin',
    stateRegion: 'TX',
    postalCode: '78704',
    countryCode: 'US',
  },
  latitude: null,
  longitude: null,
  plannedMaterialsCount: 1,
  date: '2026-10-07',
  timezone: 'Pacific/Auckland',
  dispatchNote: 'Gate code 4411',
};

describe('VisitDetail', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  async function open(roleCode = 'technician'): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'today/visits/:visitId', component: VisitDetail }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: { id: 'u-1', firstName: 'Carlos', lastName: 'Rivera', email: 'c@example.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: 'Role' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today/visits/v-1');
    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  afterEach(() => httpTesting.verify());

  it('shows the read-only job with the actual status, dispatch note and a link back (AC-10, AC-11)', async () => {
    const root = await open();
    httpTesting.expectOne({ method: 'GET', url: URL }).flush(DETAIL);
    await settle();

    const content = root.textContent!.replace(/\s+/g, ' ');
    expect(root.querySelector('h1')?.textContent).toBe('Kitchen sink leak repair');
    for (const expected of [
      'In progress',
      'Urgent',
      '#WO-3091',
      'Wed, Oct 7',
      '9:30 AM – 11:30 AM',
      'Sofia Martinez',
      '1842 Oak Street, Austin, TX 78704',
      '1 planned material',
      'Dispatch note',
      'Gate code 4411',
      'Job execution is coming soon.',
    ]) {
      expect(content).toContain(expected);
    }
    expect(content).not.toContain('Next');
    expect(content).not.toContain('Arrival');
    expect(root.querySelector('a[href^="tel:"]')?.getAttribute('href')).toBe('tel:5125550100');
    expect(root.querySelector('a[target="_blank"]')?.getAttribute('aria-label')).toContain(
      'opens in a new tab',
    );
    const back = Array.from(root.querySelectorAll('a')).find((a) =>
      a.textContent?.includes("Back to Today's jobs"),
    );
    expect(back?.getAttribute('href')).toBe('/today');
  });

  it.each([
    [404, undefined, "This job isn't available.", true],
    [
      404,
      'technician_profile_not_linked',
      "Your team profile isn't linked yet. Contact your manager.",
      false,
    ],
    [
      403,
      'technician_inactive',
      'Your technician profile is inactive. Contact your manager.',
      false,
    ],
    [500, undefined, "We couldn't load this job. Try again.", false],
  ])('shows the %i state (%s) as "%s" (AC-03, AC-11)', async (status, code, message, hasBack) => {
    const root = await open();
    httpTesting.expectOne(URL).flush({ status, code }, { status, statusText: 'Error' });
    await settle();

    expect(root.textContent).toContain(message);
    expect(root.textContent?.includes("Back to Today's jobs")).toBe(hasBack);
    expect(root.querySelector('h1')).toBeNull();
  });

  it('shows the forbidden state without a request for other roles (AC-03)', async () => {
    const root = await open('dispatcher');

    expect(root.textContent).toContain("You don't have access to Today's jobs.");
    httpTesting.expectNone(URL);
  });
});
