import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { injectHelpContext } from '../../utils/help-audience';
import { supportMailto } from '../../utils/help-content';

/** "Still need help?" card with the email-based Contact support action (FR-11). */
@Component({
  selector: 'app-help-support-card',
  imports: [TranslocoPipe, ButtonDirective, RouterLink],
  templateUrl: './help-support-card.html',
  styleUrl: './help-support-card.scss',
})
export class HelpSupportCard {
  private readonly context = injectHelpContext();

  /** FR-17: portal customers are pointed to their service provider, never to a mailbox. */
  protected readonly portal = this.context.audience === 'portal';
  protected readonly base = this.context.base;
  /** Home variant: filled button, View all help articles and the email note. */
  readonly home = input(false);
  /** Subject suffix when opened from an article. */
  readonly articleTitle = input<string | undefined>(undefined);

  protected readonly mailto = computed(() => supportMailto(this.articleTitle()));
}
