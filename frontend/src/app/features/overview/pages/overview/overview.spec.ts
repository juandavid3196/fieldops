import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { Overview } from './overview';

const API_BASE_URL = 'http://api.test';

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
};

describe('Overview', () => {
  it('shows only the welcome heading and organization/role details, without a landmark or actions (FR-07, AC-15)', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([{ path: 'overview', component: Overview }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API_BASE_URL}/sessions/current`).flush(SESSION);

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/overview', Overview);
    const host = harness.routeNativeElement as HTMLElement;

    expect(host.querySelector('h1')?.textContent?.trim()).toBe('Welcome, Sofia');
    expect(Array.from(host.querySelectorAll('dt'), (node) => node.textContent?.trim())).toEqual([
      'Organization',
      'Role',
    ]);
    expect(Array.from(host.querySelectorAll('dd'), (node) => node.textContent?.trim())).toEqual([
      'Acme Services',
      'Owner',
    ]);
    expect(host.querySelector('main, button, a, p-message')).toBeNull();
    httpTesting.verify();
  });
});
