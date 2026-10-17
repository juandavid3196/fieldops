import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { HelpTopic } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';

/** Link card to a topic: icon, title, card subtitle and chevron (home and Related topics). */
@Component({
  selector: 'app-help-topic-card',
  imports: [RouterLink],
  templateUrl: './help-topic-card.html',
  styleUrl: './help-topic-card.scss',
})
export class HelpTopicCard {
  protected readonly base = injectHelpContext().base;
  readonly topic = input.required<HelpTopic>();
}
