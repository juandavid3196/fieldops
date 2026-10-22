import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../../../core/config/api.config';
import { CustomersService } from './customers.service';

const API = 'http://api.test';
const ID = '3f2b8c1e-5a4d-4e6f-9a7b-1c2d3e4f5a6b';

describe('CustomersService related records', () => {
  let service: CustomersService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(CustomersService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  const sent = (path: string): Record<string, string> => {
    const req = httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}/${path}`);
    req.flush({});
    return Object.fromEntries(
      req.request.params.keys().map((k) => [k, req.request.params.get(k)!]),
    );
  };

  it.each(['requests', 'quotes', 'jobs', 'invoices'] as const)(
    'sends the %s list query: trimmed search and status only when set, page and pageSize always (BR-04)',
    (kind) => {
      service
        .relatedList(ID, kind, { search: ' leak ', status: 'draft', page: 2, pageSize: 25 })
        .subscribe();
      expect(sent(`customers/${ID}/${kind}`)).toEqual({
        search: 'leak',
        status: 'draft',
        page: '2',
        pageSize: '25',
      });
      service
        .relatedList(ID, kind, { search: '   ', status: null, page: 1, pageSize: 10 })
        .subscribe();
      expect(sent(`customers/${ID}/${kind}`)).toEqual({ page: '1', pageSize: '10' });
    },
  );

  it('sends the activity filters and the opaque cursor, omitting type for all activity (BR-12)', () => {
    service.activity(ID, { type: 'all', period: 'last_90_days' }).subscribe();
    expect(sent(`customers/${ID}/activity`)).toEqual({ period: 'last_90_days' });
    service
      .activity(ID, { type: 'invoices', period: 'last_7_days', before: 'cursor-1' })
      .subscribe();
    expect(sent(`customers/${ID}/activity`)).toEqual({
      type: 'invoices',
      period: 'last_7_days',
      before: 'cursor-1',
    });
  });
});
