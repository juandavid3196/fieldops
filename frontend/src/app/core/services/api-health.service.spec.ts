import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { HealthCheckResponse } from '../models/health-check.model';
import { ApiHealthService } from './api-health.service';

describe('ApiHealthService', () => {
  it('requests the health endpoint under the configured API base URL', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    const httpTesting = TestBed.inject(HttpTestingController);
    const response: HealthCheckResponse = { status: 'Healthy', totalDurationMs: 3, checks: [] };
    let received: HealthCheckResponse | undefined;

    TestBed.inject(ApiHealthService)
      .check()
      .subscribe((result) => (received = result));

    const request = httpTesting.expectOne('http://api.test/health');
    expect(request.request.method).toBe('GET');
    request.flush(response);

    expect(received).toEqual(response);
    httpTesting.verify();
  });
});
