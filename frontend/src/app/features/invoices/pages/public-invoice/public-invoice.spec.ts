import { Location } from '@angular/common';
import { HttpRequest, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../../../../app.routes';
import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { PublicInvoice } from '../../models/invoice.model';
import { previewBody } from '../../testing/invoice-fixtures';
import { PublicInvoicePage } from './public-invoice';

const API = 'http://api.test';
const BASE = `${API}/public/invoice-links`;
const STORAGE_KEY = 'fieldops.invoice-link-token';
const TOKEN = 'Abc_123-'.repeat(5) + 'xyz';

const publicInvoice = (hasLogo = false): PublicInvoice => ({
  ...previewBody(),
  organization: { ...previewBody().organization, hasLogo },
  status: 'sent',
});

describe('PublicInvoicePage', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let urlAtRequest: string[];

  async function setup(url: string): Promise<void> {
    urlAtRequest = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter(routes),
        provideHttpClient(
          withInterceptors([
            (request: HttpRequest<unknown>, next) => {
              urlAtRequest.push(TestBed.inject(Location).path(true));
              return next(request);
            },
            authInterceptor,
            errorInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl(url, PublicInvoicePage);
    await stable();
  }

  const stable = () => harness.fixture.whenStable();
  const text = () => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string) =>
    Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const request = (path: string): Promise<TestRequest> =>
    vi.waitFor(() => httpTesting.expectOne(`${BASE}/${path}`));
  async function respond(pending: TestRequest, body: object | null, status = 200): Promise<void> {
    pending.flush(body, { status, statusText: status === 200 ? 'OK' : 'Error' });
    await stable();
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('captures the fragment token, replaces the URL before any request, sends the token only in POST bodies and renders a strictly read-only invoice with Download PDF (FR-10, AC-22)', async () => {
    const downloads: string[] = [];
    URL.createObjectURL = vi.fn(() => 'blob:public');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });
    await setup(`/invoices/view#token=${TOKEN}&other=1`);

    const view = await request('view');
    expect(view.request.method).toBe('POST');
    expect(view.request.body).toEqual({ token: TOKEN });
    expect(view.request.url).not.toContain(TOKEN);
    expect(urlAtRequest).toEqual(['/invoices/view']);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    expect(TestBed.inject(Location).path(true)).toBe('/invoices/view');
    await respond(view, publicInvoice(true));

    const logo = await request('logo');
    expect(logo.request.body).toEqual({ token: TOKEN });
    logo.flush(new Blob(['png'], { type: 'image/png' }));
    await stable();
    expect(document.body.querySelector('img.page__logo')?.getAttribute('src')).toBe('blob:public');

    for (const expected of [
      'Invoice INV-1048',
      'Northstar Home Services',
      'Total due$287.30',
      'Service completion note',
      'Thank you for your business!',
      'Online payment will be available soon.',
      'Powered by FieldOps',
    ]) {
      expect(text()).toContain(expected);
    }
    // No inputs, no links (no work order link) and no action besides Download PDF.
    expect(document.body.querySelectorAll('input, textarea, select, a')).toHaveLength(0);
    expect(
      Array.from(document.body.querySelectorAll('button'), (b) => b.textContent?.trim()),
    ).toEqual(['Download PDF']);

    button('Download PDF')!.click();
    await stable();
    const pdf = await request('pdf');
    expect(pdf.request.body).toEqual({ token: TOKEN });
    pdf.flush(new Blob(['%PDF']));
    await stable();
    expect(downloads).toEqual(['INV-1048.pdf']);

    button('Download PDF')!.click();
    await stable();
    (await request('pdf')).error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await stable();
    expect(text()).toContain("We couldn't download the PDF. Please try again.");
    expect(text()).toContain('Invoice INV-1048');
  });

  it.each([
    [
      'no token (no request)',
      '/invoices/view',
      0,
      "This invoice link isn't available. It may have expired or been replaced. Please contact the company that sent it.",
    ],
    [
      'unavailable token',
      `/invoices/view#token=${TOKEN}`,
      404,
      "This invoice link isn't available. It may have expired or been replaced. Please contact the company that sent it.",
    ],
    [
      'rate limited',
      `/invoices/view#token=${TOKEN}`,
      429,
      'Too many attempts. Please wait a few minutes and try again.',
    ],
    [
      'server failure',
      `/invoices/view#token=${TOKEN}`,
      500,
      "We couldn't load this invoice. Try again.",
    ],
  ])(
    'shows the generic %s state without invoice data (FR-10, AC-22)',
    async (_name, url, status, expected) => {
      await setup(url);
      if (status !== 0) {
        await respond(await request('view'), { title: 'backend text', code: 'x' }, status);
      }

      expect(text()).toContain(expected);
      expect(text()).not.toContain('backend text');
      expect(text()).not.toContain('Northstar');
      expect(document.activeElement?.tagName).toBe('H1');
      expect(document.body.querySelector('table')).toBeNull();
      if (status === 404) {
        expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      }
      if (status === 429 || status === 500) {
        button('Retry')!.click();
        await stable();
        await respond(await request('view'), publicInvoice());
        expect(text()).toContain('Invoice INV-1048');
      }
    },
  );
});
