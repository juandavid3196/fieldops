import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { HelpBreadcrumb } from '../../components/help-breadcrumb/help-breadcrumb';
import { HelpSearchField } from '../../components/help-search-field/help-search-field';
import { HelpSupportCard } from '../../components/help-support-card/help-support-card';
import { HelpTopicCard } from '../../components/help-topic-card/help-topic-card';
import { HelpArticle, HelpArticleRef } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';
import { findTopic, topicArticles, topicsFor } from '../../utils/help-content';
import { effectiveQuery, searchArticles } from '../../utils/help-search';
import { HelpNotFound } from '../help-not-found/help-not-found';

import { t } from '../../../../core/i18n/translate';
/** `/help/:topicSlug` (FR-07, FR-08): articles by section, topic search, popular and related. */
@Component({
  selector: 'app-help-topic',
  imports: [
    TranslocoPipe,
    HelpBreadcrumb,
    HelpNotFound,
    HelpSearchField,
    HelpSupportCard,
    HelpTopicCard,
    RouterLink,
  ],
  templateUrl: './help-topic.html',
  styleUrl: './help-topic.scss',
})
export class HelpTopicPage {
  private readonly route = inject(ActivatedRoute);
  private readonly context = injectHelpContext();

  readonly base = this.context.base;

  private readonly slug = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('topicSlug'))),
    { initialValue: null },
  );

  readonly topic = computed(() => findTopic(this.slug(), this.context.audience));
  readonly query = signal('');
  readonly trimmedQuery = computed(() => this.query().trim());
  readonly searching = computed(() => effectiveQuery(this.query()) !== null);

  readonly crumbs = computed(() => [
    { label: t('help.helpTopic.help'), link: this.base },
    { label: this.topic()?.title ?? '' },
  ]);

  /** Sections keep their order; sections without matches are hidden (BR-06, FR-08). */
  readonly sections = computed(() => {
    const topic = this.topic();
    if (topic === null) {
      return [];
    }
    const matches = new Set<HelpArticle>(
      searchArticles(
        topicArticles(topic).map((article): HelpArticleRef => ({ topic, article })),
        this.query(),
      ).map((result) => result.article),
    );
    return topic.sections
      .map((section) => ({
        label: section.label,
        articles: section.articles.filter((article) => matches.has(article)),
      }))
      .filter((section) => section.articles.length > 0);
  });

  readonly popular = computed(() => {
    const topic = this.topic();
    const articles = topic === null ? [] : topicArticles(topic);
    return (topic?.popular ?? []).flatMap((slug) => articles.find((a) => a.slug === slug) ?? []);
  });

  /** Other topics of the same audience in catalog order (BR-13). */
  readonly related = computed(() =>
    topicsFor(this.context.audience).filter((topic) => topic !== this.topic()),
  );

  readonly searchPlaceholder = computed(() =>
    t('help.helpTopic.searchIn', { value: this.topic()?.title ?? '' }),
  );
  readonly status = computed(() => {
    if (!this.searching()) {
      return '';
    }
    const count = this.sections().reduce((total, section) => total + section.articles.length, 0);
    return count === 0
      ? t('help.helpTopic.noArticlesInMatch', {
          title: this.topic()?.title,
          trimmedQuery: this.trimmedQuery(),
        })
      : t('help.helpTopic.found', {
          count: count,
          value: count === 1 ? t('help.helpTopic.article') : t('help.helpTopic.articles2'),
        });
  });
}
