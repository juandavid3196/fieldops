import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../../../core/config/api.config';
import { NotificationUnreadService } from '../../../core/services/notification-unread.service';
import { NotificationsService } from './notifications.service';

const BASE = 'http://api.test/notifications';

describe('NotificationsService', () => {
  let service: NotificationsService;
  let unread: NotificationUnreadService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(NotificationsService);
    unread = TestBed.inject(NotificationUnreadService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('sends only set list filters and updates the bell from every mark response with no extra GET (AC-15, AC-20)', () => {
    service
      .list({
        tab: 'unread',
        q: 'quote',
        type: null,
        branchId: 'br-1',
        date: '7d',
        page: 2,
        pageSize: 25,
      })
      .subscribe();
    const list = httpTesting.expectOne((request) => request.url === BASE);
    expect(list.request.params.keys().sort()).toEqual(
      ['branchId', 'date', 'page', 'pageSize', 'q', 'tab'].sort(),
    );
    list.flush({});

    service.read('n 1').subscribe();
    httpTesting
      .expectOne({ method: 'POST', url: `${BASE}/n%201/read` })
      .flush({ summary: { unread: 5, today: 1 } });
    expect(unread.count()).toBe(5);

    service.unreadOne('n-1').subscribe();
    httpTesting
      .expectOne({ method: 'POST', url: `${BASE}/n-1/unread` })
      .flush({ summary: { unread: 6, today: 1 } });
    expect(unread.count()).toBe(6);

    service.readAll().subscribe();
    httpTesting
      .expectOne({ method: 'POST', url: `${BASE}/read-all` })
      .flush({ updated: 6, summary: { unread: 0, today: 1 } });
    expect(unread.count()).toBe(0);
  });
});
