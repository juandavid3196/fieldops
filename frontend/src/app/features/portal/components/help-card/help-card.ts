import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { telHref } from '../../utils/portal-format';

/** Need help card (BR-35): Call (phone known) and Send a message (organization can receive). */
@Component({
  selector: 'app-help-card',
  imports: [ButtonDirective],
  templateUrl: './help-card.html',
  styleUrl: '../../portal.scss',
  host: { id: 'help', tabindex: '-1' },
})
export class HelpCard {
  readonly phone = input<string | null>(null);
  readonly canMessage = input(false);
  readonly message = output<void>();

  protected readonly href = telHref;
}
