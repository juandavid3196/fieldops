import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { DemoModeService } from '../services/demo-mode.service';
import { demoModeInterceptor } from './demo-mode.interceptor';
import { errorInterceptor } from './error.interceptor';

const API_BASE_URL = 'http://api.test';

describe('demoModeInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let demo: DemoModeService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(withInterceptors([demoModeInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    demo = TestBed.inject(DemoModeService);
  });

  afterEach(() => httpTesting.verify());

  it('flags only the read-only demo refusal and keeps the error flowing', () => {
    let failed = 0;
    const onError = () => (failed += 1);

    http.post(`${API_BASE_URL}/branches`, {}).subscribe({ error: onError });
    httpTesting
      .expectOne(`${API_BASE_URL}/branches`)
      .flush({ status: 403, code: 'demo_read_only' }, { status: 403, statusText: 'Forbidden' });

    http.post(`${API_BASE_URL}/branches`, {}).subscribe({ error: onError });
    httpTesting
      .expectOne(`${API_BASE_URL}/branches`)
      .flush({ status: 403, code: 'other' }, { status: 403, statusText: 'Forbidden' });

    expect(failed).toBe(2);
    expect(demo.refusals()).toBe(1);
  });
});
