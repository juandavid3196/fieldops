import { Component } from '@angular/core';

/**
 * Public placeholder of the emailed quote link (BR-27): organization-neutral, makes no API call
 * and never reads the URL fragment that carries the token. Replaced by Customer Quote Approval.
 */
@Component({
  selector: 'app-quote-approval',
  template: `
    <main class="approval">
      <p class="approval__brand">FieldOps</p>
      <h1 class="approval__message">This quote will be available soon.</h1>
    </main>
  `,
  styleUrl: './quote-approval.scss',
})
export class QuoteApproval {}
