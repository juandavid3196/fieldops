import { Component } from '@angular/core';

/** Decorative left panel of the portal sign-in: wordmark, headline and floating module cards. */
@Component({
  selector: 'app-portal-auth-brand',
  templateUrl: './portal-auth-brand.html',
  styleUrl: './portal-auth-brand.scss',
})
export class PortalAuthBrand {
  protected readonly cards = [
    { name: 'Quotes', icon: 'pi-check', tone: 'teal' },
    { name: 'Appointments', icon: 'pi-calendar', tone: 'blue' },
    { name: 'Invoices', icon: 'pi-file', tone: 'teal' },
  ] as const;
}
