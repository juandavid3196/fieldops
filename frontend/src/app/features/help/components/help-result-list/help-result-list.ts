import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { HelpArticleRef } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';

/** Result-list presentation: article title, description and topic title; opens the article. */
@Component({
  selector: 'app-help-result-list',
  imports: [RouterLink],
  templateUrl: './help-result-list.html',
  styleUrl: './help-result-list.scss',
})
export class HelpResultList {
  protected readonly base = injectHelpContext().base;
  readonly results = input.required<readonly HelpArticleRef[]>();
}
