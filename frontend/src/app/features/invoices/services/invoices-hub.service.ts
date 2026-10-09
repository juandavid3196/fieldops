import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { DownloadedCsv } from '../../../shared/utils/csv-file';
import {
  InvoiceFilters,
  InvoiceRow,
  InvoicesOptions,
  NamedOption,
  OverviewResponse,
  Page,
  PaymentFilters,
  PaymentRow,
  RecordPaymentBody,
  RecordPaymentResult,
} from '../models/invoices-hub.model';

/** Invoices and payments hub endpoints (Final contract: specs/invoices-payments-management). */
@Injectable({ providedIn: 'root' })
export class InvoicesHubService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, path === '' ? 'invoices' : `invoices/${path}`);
  }

  options(): Observable<InvoicesOptions> {
    return this.http.get<InvoicesOptions>(this.url('options'));
  }

  customers(search: string): Observable<readonly NamedOption[]> {
    const term = search.trim();
    return this.http.get<readonly NamedOption[]>(this.url('customers'), {
      params: term === '' ? new HttpParams() : new HttpParams().set('search', term),
    });
  }

  overview(branchId: string | null): Observable<OverviewResponse> {
    return this.http.get<OverviewResponse>(this.url('overview'), {
      params: branchId === null ? new HttpParams() : new HttpParams().set('branchId', branchId),
    });
  }

  invoices(filters: InvoiceFilters, page: number): Observable<Page<InvoiceRow>> {
    return this.http.get<Page<InvoiceRow>>(this.url(''), {
      params: invoiceParams(filters).set('page', page),
    });
  }

  payments(filters: PaymentFilters, page: number): Observable<Page<PaymentRow>> {
    return this.http.get<Page<PaymentRow>>(this.url('payments'), {
      params: paymentParams(filters).set('page', page),
    });
  }

  /** CSV of the Invoices tab with its current filters (no page). */
  exportInvoices(filters: InvoiceFilters, fileDate: string): Observable<DownloadedCsv> {
    return this.download('export', invoiceParams(filters), `invoices-${fileDate}.csv`);
  }

  /** CSV of the Payments tab with its current filters (no page). */
  exportPayments(filters: PaymentFilters, fileDate: string): Observable<DownloadedCsv> {
    return this.download('payments/export', paymentParams(filters), `payments-${fileDate}.csv`);
  }

  /** `201` created or `200` already recorded; `changed` tells which. */
  recordPayment(invoiceId: string, body: RecordPaymentBody): Observable<RecordPaymentResult> {
    return this.http.post<RecordPaymentResult>(
      this.url(`${encodeURIComponent(invoiceId)}/payments`),
      body,
    );
  }

  private download(path: string, params: HttpParams, fallback: string): Observable<DownloadedCsv> {
    return this.http
      .get(this.url(path), { params, responseType: 'blob', observe: 'response' })
      .pipe(map((response) => toFile(response, fallback)));
  }
}

function withCommon(
  params: HttpParams,
  filters: { search: string; from: string; to: string; branchId: string | null },
): HttpParams {
  let result = params;
  const search = filters.search.trim();
  if (search !== '') {
    result = result.set('search', search);
  }
  if (filters.from !== '') {
    result = result.set('from', filters.from);
  }
  if (filters.to !== '') {
    result = result.set('to', filters.to);
  }
  return filters.branchId === null ? result : result.set('branchId', filters.branchId);
}

function invoiceParams(filters: InvoiceFilters): HttpParams {
  let result = withCommon(new HttpParams(), filters);
  if (filters.status !== null) {
    result = result.set('status', filters.status);
  }
  return filters.customerId === null ? result : result.set('customerId', filters.customerId);
}

function paymentParams(filters: PaymentFilters): HttpParams {
  const result = withCommon(new HttpParams(), filters);
  return filters.method === null ? result : result.set('method', filters.method);
}

function toFile(response: HttpResponse<Blob>, fallbackName: string): DownloadedCsv {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
