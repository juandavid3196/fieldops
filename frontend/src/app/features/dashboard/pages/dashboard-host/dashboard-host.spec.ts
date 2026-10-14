import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { DashboardHost } from './dashboard-host';

const API = 'http://api.test';

describe('Dashboard host', () => {
  // AC-01, AC-02, AC-03: the view each role gets, the switch and the data requests that follow.
  it.each([
    ['owner', '/dashboard', 'Business Overview', true, '/business-overview/options', {}],
    [
      'owner',
      '/dashboard?view=operations',
      'Operations Dashboard',
      true,
      '/operations-dashboard/options',
      { view: 'operations' },
    ],
    [
      'owner',
      '/dashboard?view=nope&date=2026-01-01',
      'Business Overview',
      true,
      '/business-overview/options',
      { view: 'business' },
    ],
    [
      'dispatcher',
      '/dashboard?view=business',
      'Operations Dashboard',
      false,
      '/operations-dashboard/options',
      {},
    ],
    [
      'operations_manager',
      '/dashboard',
      'Operations Dashboard',
      false,
      '/operations-dashboard/options',
      {},
    ],
    ['accounting', '/dashboard', "You don't have access to Dashboard.", false, null, {}],
  ])(
    'resolves the %s view for %s',
    async (role, url, expectedText, hasSwitch, optionsPath, expectedQuery) => {
      TestBed.configureTestingModule({
        providers: [
          { provide: API_CONFIG, useValue: { baseUrl: API } },
          provideRouter([{ path: 'dashboard', component: DashboardHost }]),
          provideHttpClient(withInterceptors([errorInterceptor])),
          provideHttpClientTesting(),
        ],
      });
      const httpTesting = TestBed.inject(HttpTestingController);
      TestBed.inject(SessionService).loadCurrent().subscribe();
      httpTesting.expectOne(`${API}/sessions/current`).flush({
        user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
        organization: { id: 'o-1', name: 'Acme' },
        role: { code: role, name: role },
      });
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl(url);
      harness.detectChanges();
      await harness.fixture.whenStable();
      harness.detectChanges();

      const element = harness.routeNativeElement as HTMLElement;
      expect(element.textContent).toContain(expectedText);
      expect(element.querySelector('app-dashboard-view-switch') !== null).toBe(hasSwitch);
      // Only the shown view requests data; roles without access request nothing.
      const requests = httpTesting.match(() => true).map((request) => request.request.url);
      expect(requests).toEqual(optionsPath === null ? [] : [`${API}${optionsPath}`]);
      // An invalid or foreign `view` is replaced in the URL (replaceUrl).
      expect(TestBed.inject(Router).parseUrl(TestBed.inject(Router).url).queryParams['view']).toBe(
        (expectedQuery as { view?: string }).view,
      );
    },
  );
});
