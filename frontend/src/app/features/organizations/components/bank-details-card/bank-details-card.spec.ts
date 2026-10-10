import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { BankDetails } from '../../models/bank-details.model';
import { BankDetailsCard } from './bank-details-card';

const API = 'http://api.test';
const URL = `${API}/organization-settings/bank-details`;

const EMPTY: BankDetails = {
  configured: false,
  bankName: null,
  accountNumberMasked: null,
  routingNumber: null,
  updatedAt: null,
};
const SAVED: BankDetails = {
  configured: true,
  bankName: 'FieldOps Payments',
  accountNumberMasked: '•••• 4821',
  routingNumber: '021000021',
  updatedAt: '2026-10-09T15:00:00Z',
};

describe('BankDetailsCard', () => {
  let fixture: ComponentFixture<BankDetailsCard>;
  let card: BankDetailsCard;
  let httpTesting: HttpTestingController;
  let toasts: string[];
  let unauthorized: number;

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        MessageService,
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    toasts = [];
    vi.spyOn(TestBed.inject(MessageService), 'add').mockImplementation((message) => {
      toasts.push(message.summary ?? '');
    });
    fixture = TestBed.createComponent(BankDetailsCard);
    card = fixture.componentInstance;
    unauthorized = 0;
    card.unauthorized.subscribe(() => unauthorized++);
    await stable();
  }

  const stable = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => (el().textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const field = (id: string): HTMLInputElement => el().querySelector<HTMLInputElement>(`#${id}`)!;
  const type = async (id: string, value: string): Promise<void> => {
    const control = field(id);
    control.value = value;
    control.dispatchEvent(new Event('input'));
    await stable();
  };
  const put = (): TestRequest => httpTesting.expectOne({ method: 'PUT', url: URL });
  async function respond(pending: TestRequest, body: object | null, status = 200): Promise<void> {
    pending.flush(body, { status, statusText: status < 400 ? 'OK' : 'Error' });
    await stable();
  }
  async function save(): Promise<void> {
    button('Save bank details')!.click();
    await stable();
  }

  afterEach(() => httpTesting.verify());

  it('loads the section, validates fields, saves with the masked number and Replace, and maps 400, 409, 503 and load failures (FR-13, AC-24)', async () => {
    // A failed load shows the section error with Retry.
    await setup();
    expect(el().querySelector('[aria-busy="true"]')).not.toBeNull();
    await respond(httpTesting.expectOne(URL), { code: 'bank_details_unavailable' }, 503);
    expect(text()).toContain("We couldn't load bank details.");
    button('Retry')!.click();
    await stable();
    await respond(httpTesting.expectOne(URL), EMPTY);

    // Not configured: empty fields, helper text, required account number, nothing is dirty.
    expect(text()).toContain('Bank transfer details');
    expect(text()).toContain('Customers see these details on unpaid invoices.');
    expect(field('bank-bankName').value).toBe('');
    expect(
      el().querySelector('label[for="bank-accountNumber"] .form-field__required'),
    ).not.toBeNull();
    expect(button('Replace')).toBeUndefined();
    expect(card.isDirty()).toBe(false);

    // Client validation blocks the request and focuses the first invalid field.
    await save();
    expect(text()).toContain('Enter the bank name.');
    expect(text()).toContain('Enter a valid account number.');
    expect(text()).toContain('Enter a 9-digit routing number.');
    expect(document.activeElement?.id).toBe('bank-bankName');
    await type('bank-bankName', '  FieldOps Payments  ');
    await type('bank-accountNumber', '12a');
    await type('bank-routingNumber', '0210');
    expect(card.isDirty()).toBe(true);
    await save();
    expect(text()).toContain('Enter a valid account number.');
    expect(text()).toContain('Enter a 9-digit routing number.');
    await type('bank-accountNumber', '000123454821');
    await type('bank-routingNumber', '021000021');

    // 400 field errors keep the values; the first save sends no stale token.
    await save();
    const invalid = put();
    expect(invalid.request.body).toEqual({
      bankName: 'FieldOps Payments',
      accountNumber: '000123454821',
      routingNumber: '021000021',
      updatedAt: null,
    });
    await respond(invalid, { errors: { routingNumber: ['server text'] } }, 400);
    expect(text()).toContain('Enter a 9-digit routing number.');
    expect(text()).not.toContain('server text');
    expect(field('bank-accountNumber').value).toBe('000123454821');

    // 503 and 409 messages; Refresh re-reads the details.
    await save();
    await respond(put(), { code: 'bank_details_unavailable' }, 503);
    expect(text()).toContain("Bank details can't be saved right now.");
    await save();
    await respond(put(), { code: 'bank_details_changed' }, 409);
    expect(text()).toContain('These details changed. Refresh to see the latest.');
    button('Refresh')!.click();
    await stable();
    await respond(httpTesting.expectOne(URL), EMPTY);
    expect(text()).not.toContain('These details changed');

    // Success: toast, masked number with Replace, pristine form.
    await type('bank-bankName', 'FieldOps Payments');
    await type('bank-accountNumber', '000123454821');
    await type('bank-routingNumber', '021000021');
    await save();
    await respond(put(), SAVED);
    expect(toasts).toEqual(['Bank details saved.']);
    expect(text()).toContain('•••• 4821');
    expect(el().querySelector('input#bank-accountNumber')).toBeNull();
    expect(card.isDirty()).toBe(false);

    // Not replaced: the PUT sends an empty account number with the stored `updatedAt`.
    await type('bank-bankName', 'New Bank');
    await save();
    const kept = put();
    expect(kept.request.body).toEqual({
      bankName: 'New Bank',
      accountNumber: '',
      routingNumber: '021000021',
      updatedAt: '2026-10-09T15:00:00Z',
    });
    await respond(kept, { ...SAVED, bankName: 'New Bank' });

    // Replace reveals an empty input that is validated when filled.
    button('Replace')!.click();
    await stable();
    expect(field('bank-accountNumber').value).toBe('');
    expect(document.activeElement?.id).toBe('bank-accountNumber');
    await type('bank-accountNumber', '999');
    await save();
    expect(text()).toContain('Enter a valid account number.');
    await type('bank-accountNumber', '98765432');
    await save();
    const replaced = put();
    expect((replaced.request.body as { accountNumber: string }).accountNumber).toBe('98765432');
    await respond(replaced, null, 401);
    expect(unauthorized).toBe(1);
  });
});
