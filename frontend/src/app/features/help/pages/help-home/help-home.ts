import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AccordionModule } from 'primeng/accordion';
import { map } from 'rxjs';

import { HelpBreadcrumb } from '../../components/help-breadcrumb/help-breadcrumb';
import { HelpResultList } from '../../components/help-result-list/help-result-list';
import { HelpSearchField } from '../../components/help-search-field/help-search-field';
import { HelpSupportCard } from '../../components/help-support-card/help-support-card';
import { HelpTopicCard } from '../../components/help-topic-card/help-topic-card';
import { HelpArticleRef } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';
import {
  allArticles,
  findArticle,
  popularQuestionsFor,
  topicArticles,
  topicsFor,
} from '../../utils/help-content';
import { effectiveQuery, searchArticles } from '../../utils/help-search';

/** `/help` (FR-03 to FR-06): browse, global search, popular questions and `?view=all`. */
@Component({
  selector: 'app-help-home',
  imports: [
    AccordionModule,
    HelpBreadcrumb,
    HelpResultList,
    HelpSearchField,
    HelpSupportCard,
    HelpTopicCard,
    RouterLink,
  ],
  templateUrl: './help-home.html',
  styleUrl: './help-home.scss',
})
export class HelpHome {
  private readonly route = inject(ActivatedRoute);

  private readonly context = injectHelpContext();

  /** BR-13: only the topics, articles and questions of this route's audience. */
  readonly base = this.context.base;
  readonly topics = topicsFor(this.context.audience);
  readonly crumbs = [{ label: 'Help' }];
  readonly query = signal('');
  /** Several questions may be open at once (FR-06). */
  readonly openQuestions = signal<string[]>([]);

  readonly showAll = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('view') === 'all')),
    { initialValue: false },
  );
  readonly searching = computed(() => effectiveQuery(this.query()) !== null);
  readonly results = computed(() => searchArticles(allArticles(this.topics), this.query()));
  readonly trimmedQuery = computed(() => this.query().trim());
  /** BR-06: result counts announced politely; empty while no filter applies. */
  readonly status = computed(() => {
    if (!this.searching()) {
      return '';
    }
    const count = this.results().length;
    return count === 0
      ? `No articles match "${this.trimmedQuery()}"`
      : `${count} ${count === 1 ? 'article' : 'articles'} found`;
  });

  /** FR-05: all articles grouped by topic in catalog order. */
  readonly groups = this.topics.map((topic) => ({
    topic,
    results: topicArticles(topic).map((article): HelpArticleRef => ({ topic, article })),
  }));

  readonly questions = popularQuestionsFor(this.context.audience).map((entry) => ({
    ...entry,
    target: findArticle(entry.topicSlug, entry.articleSlug, this.context.audience),
  }));
}
