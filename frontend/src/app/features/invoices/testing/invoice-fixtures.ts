import { InvoiceDetail, PublicInvoice } from '../models/invoice.model';

/** Contract-shaped bodies shared by the invoice specs (specs/invoice-draft-delivery/spec.md). */
export const previewBody = (): Omit<PublicInvoice, 'status'> => ({
  number: 'INV-1048',
  issueDate: '2026-10-06',
  dueDate: '2026-10-06',
  paymentTerms: 'due_upon_receipt',
  currency: 'USD',
  timezone: 'America/Chicago',
  organization: {
    name: 'Northstar Home Services',
    addressLines: ['1234 Service Way', 'Austin, TX 78701'],
    phone: '(512) 555-0199',
    email: 'hello@northstar.example',
    hasLogo: false,
  },
  billTo: {
    name: 'Sofia Martinez',
    email: 'sofia@example.com',
    phone: '(512) 555-7832',
    addressLines: ['55 Billing Road'],
  },
  serviceAddress: '742 Maple Ave, Austin, TX 78704',
  workOrderNumber: 'WO-1048',
  lines: [
    {
      description: 'Plumbing service',
      detail: 'Diagnose and repair kitchen sink leak',
      quantity: '2.000',
      unit: 'hr',
      unitPrice: 120,
      taxRate: 8.25,
      amount: 240,
    },
    {
      description: 'Brass shut-off valve',
      detail: null,
      quantity: '1',
      unit: 'unit',
      unitPrice: 32.5,
      taxRate: 0,
      amount: 32.5,
    },
  ],
  totals: {
    subtotal: 272.5,
    discountTotal: 5,
    taxLabel: 'Tax (8.25%)',
    taxTotal: 19.8,
    total: 287.3,
  },
  completionNote: 'Kitchen sink leak repaired and tested.',
});

export const invoiceBody = (patch: Partial<InvoiceDetail> = {}): InvoiceDetail => ({
  ...previewBody(),
  id: 'inv-1',
  status: 'draft',
  workOrderId: 'wo-1',
  customerName: 'Sofia Martinez',
  delivery: {
    recipientEmail: 'sofia@example.com',
    message: 'Hi Sofia,\n\nHere is your invoice.',
    saved: false,
  },
  checks: [
    { key: 'recipient', met: true, label: 'Recipient email added' },
    { key: 'tax', met: true, label: 'Tax checked (Tax (8.25%))' },
  ],
  createdAt: '2026-10-06T19:14:00Z',
  createdByName: 'Daniel Kim',
  sentAt: null,
  canAct: true,
  updatedAt: '2026-10-06T19:14:00Z',
  ...patch,
});

export const sentBody = (patch: Partial<InvoiceDetail> = {}): InvoiceDetail =>
  invoiceBody({
    status: 'sent',
    sentAt: '2026-10-08T20:30:00Z',
    updatedAt: '2026-10-08T20:30:00Z',
    delivery: { recipientEmail: 'sofia@example.com', message: 'Hi Sofia', saved: true },
    ...patch,
  });
