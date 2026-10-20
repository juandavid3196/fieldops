import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { HelpResultList } from '../../components/help-result-list/help-result-list';
import { HelpSearchField } from '../../components/help-search-field/help-search-field';
import { HelpSupportCard } from '../../components/help-support-card/help-support-card';
import { HelpTopic } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';
import {
  allArticles,
  findArticle,
  findTopic,
  popularQuestionsFor,
  topicArticles,
  topicsFor,
} from '../../utils/help-content';
import { effectiveQuery, searchArticles } from '../../utils/help-search';

/** Topics featured on the public page, in display order; search covers every internal topic. */
const FEATURED_TOPICS = [
  'getting-started',
  'requests-quotes-jobs',
  'scheduling-team',
  'invoices-payments',
  'account-administration',
];

/** Public `/support` home linked from sign-in: hero search, featured topics, popular questions. */
@Component({
  selector: 'app-public-help',
  imports: [HelpResultList, HelpSearchField, HelpSupportCard, RouterLink],
  templateUrl: './public-help.html',
  styleUrl: './public-help.scss',
})
export class PublicHelp {
  private readonly context = injectHelpContext();

  readonly base = this.context.base;
  readonly topics = FEATURED_TOPICS.map((slug) => findTopic(slug, this.context.audience))
    .filter((topic): topic is HelpTopic => topic !== null)
    .map((topic) => ({ topic, count: topicArticles(topic).length }));
  readonly query = signal('');

  readonly searching = computed(() => effectiveQuery(this.query()) !== null);
  private readonly articles = allArticles(topicsFor(this.context.audience));
  readonly results = computed(() => searchArticles(this.articles, this.query()));
  readonly trimmedQuery = computed(() => this.query().trim());
  readonly status = computed(() => {
    if (!this.searching()) {
      return '';
    }
    const count = this.results().length;
    return count === 0
      ? `No articles match "${this.trimmedQuery()}"`
      : `${count} ${count === 1 ? 'article' : 'articles'} found`;
  });

  readonly questions = popularQuestionsFor(this.context.audience)
    .filter((entry) => findArticle(entry.topicSlug, entry.articleSlug, this.context.audience))
    .map((entry) => ({
      question: entry.question,
      link: [this.base, entry.topicSlug, entry.articleSlug],
    }));
}
