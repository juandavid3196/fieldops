import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { telHref } from '../../utils/portal-format';

/** Need help card (BR-35): Call (phone known) and Send a message (organization can receive). */
@Component({
  selector: 'app-help-card',
  imports: [ButtonDirective],
  templateUrl: './help-card.html',
  styleUrls: ['../../portal.scss', './help-card.scss'],
  host: { id: 'help', tabindex: '-1' },
})
export class HelpCard {
  readonly phone = input<string | null>(null);
  readonly canMessage = input(false);
  readonly composeMessage = output<void>();

  protected readonly href = telHref;
}
