import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { injectHelpContext } from '../../utils/help-audience';

/** FR-12: unknown topic, unknown article or an article under the wrong topic or audience. */
@Component({
  selector: 'app-help-not-found',
  imports: [RouterLink],
  templateUrl: './help-not-found.html',
  styleUrl: './help-not-found.scss',
})
export class HelpNotFound {
  protected readonly base = injectHelpContext().base;
}
