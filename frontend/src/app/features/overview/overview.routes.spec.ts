import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  ActivatedRouteSnapshot,
  RedirectCommand,
  RouterStateSnapshot,
  provideRouter,
} from '@angular/router';

import { API_CONFIG } from '../../core/config/api.config';
import { SessionService } from '../../core/services/session.service';
import { overviewRedirectGuard } from './overview.routes';

describe('Overview route', () => {
  it.each([
    ['owner', true],
    ['operations_manager', true],
    ['dispatcher', true],
    ['viewer', false],
    ['technician', false],
    ['accounting', false],
  ])('sends %s to the dashboard: %s (AC-01)', (role, redirected) => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(SessionService).loadCurrent().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/sessions/current')
      .flush({
        user: { id: 'u-1', firstName: 'A', lastName: 'B', email: 'a@b.com' },
        organization: { id: 'o-1', name: 'Acme' },
        role: { code: role, name: role },
      });

    const result = TestBed.runInInjectionContext(() =>
      overviewRedirectGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

    if (redirected) {
      expect(result).toBeInstanceOf(RedirectCommand);
      expect((result as RedirectCommand).redirectTo.toString()).toBe('/dashboard');
      expect((result as RedirectCommand).navigationBehaviorOptions).toEqual({ replaceUrl: true });
    } else {
      expect(result).toBe(true);
    }
  });
});
