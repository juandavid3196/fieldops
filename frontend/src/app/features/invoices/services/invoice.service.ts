import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  DeliveryBody,
  InvoiceDetail,
  PdfFile,
  ResendResult,
  SendResult,
} from '../models/invoice.model';
import { pdfFileName } from '../utils/invoice-format';

/** Internal invoice endpoints (Final contract: specs/invoice-draft-delivery/spec.md). */
@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(invoiceId: string, path = ''): string {
    return buildApiUrl(this.config, `invoices/${encodeURIComponent(invoiceId)}${path}`);
  }

  get(invoiceId: string): Observable<InvoiceDetail> {
    return this.http.get<InvoiceDetail>(this.url(invoiceId));
  }

  saveDraft(invoiceId: string, body: DeliveryBody): Observable<InvoiceDetail> {
    return this.http.put<InvoiceDetail>(this.url(invoiceId, '/draft'), body);
  }

  send(invoiceId: string, body: DeliveryBody): Observable<SendResult> {
    return this.http.post<SendResult>(this.url(invoiceId, '/send'), body);
  }

  resendEmail(invoiceId: string, updatedAt: string): Observable<ResendResult> {
    return this.http.post<ResendResult>(this.url(invoiceId, '/resend-email'), { updatedAt });
  }

  pdf(invoiceId: string, fallbackName: string): Observable<PdfFile> {
    return this.http
      .get(this.url(invoiceId, '/pdf'), { responseType: 'blob', observe: 'response' })
      .pipe(map((response: HttpResponse<Blob>) => toFile(response, fallbackName)));
  }
}

function toFile(response: HttpResponse<Blob>, fallbackName: string): PdfFile {
  return {
    blob: response.body ?? new Blob([]),
    fileName: pdfFileName(response.headers.get('Content-Disposition'), fallbackName),
  };
}
