import { environment } from '../../../../environments/environment';
import {
  HELP_PORTAL_POPULAR_QUESTIONS,
  HELP_POPULAR_QUESTIONS,
  HELP_TOPICS,
} from '../data/help-catalog';
import {
  HelpArticle,
  HelpArticleRef,
  HelpAudience,
  HelpPopularQuestion,
  HelpTopic,
} from '../models/help.model';

import { t } from '../../../core/i18n/translate';
const WORDS_PER_MINUTE = 200;
const SUPPORT_SUBJECT = (): string => t('help.helpContent.supportSubject');

/** Base path of the help center for an audience; every help link stays inside it (FR-16). */
export function helpBasePath(audience: HelpAudience): string {
  return audience === 'portal' ? '/portal/help' : '/help';
}

/** BR-13: the topics of one audience, in catalog order. */
export function topicsFor(audience: HelpAudience): readonly HelpTopic[] {
  return HELP_TOPICS.filter((topic) => topic.audience === audience);
}

/** BR-05, BR-13: the popular questions of one audience, in order. */
export function popularQuestionsFor(audience: HelpAudience): readonly HelpPopularQuestion[] {
  return audience === 'portal' ? HELP_PORTAL_POPULAR_QUESTIONS : HELP_POPULAR_QUESTIONS;
}

/** Every article in catalog order (topic, then section, then article). */
export function allArticles(topics: readonly HelpTopic[] = HELP_TOPICS): HelpArticleRef[] {
  return topics.flatMap((topic) => topicArticles(topic).map((article) => ({ topic, article })));
}

export function topicArticles(topic: HelpTopic): HelpArticle[] {
  return topic.sections.flatMap((section) => section.articles);
}

/** A topic of the given audience only; a topic of another audience is not found (BR-13). */
export function findTopic(slug: string | null, audience: HelpAudience): HelpTopic | null {
  return topicsFor(audience).find((topic) => topic.slug === slug) ?? null;
}

/** The article under its own topic only; a slug under the wrong topic is not found (FR-12). */
export function findArticle(
  topicSlug: string | null,
  articleSlug: string | null,
  audience: HelpAudience,
): HelpArticleRef | null {
  const topic = findTopic(topicSlug, audience);
  const article =
    topic === null ? undefined : topicArticles(topic).find((a) => a.slug === articleSlug);
  return topic !== null && article !== undefined ? { topic, article } : null;
}

/** BR-09: previous and next within the same topic only. */
export function neighbours(
  topic: HelpTopic,
  article: HelpArticle,
): { readonly previous: HelpArticle | null; readonly next: HelpArticle | null } {
  const articles = topicArticles(topic);
  const index = articles.indexOf(article);
  return { previous: articles[index - 1] ?? null, next: articles[index + 1] ?? null };
}

/** BR-08: words of introduction, callout, steps and What happens next, over 200, rounded up, min 1. */
export function readingMinutes(article: HelpArticle): number {
  const text = [
    article.introduction,
    article.callout ?? '',
    ...article.steps.map((step) => step.text),
    article.whatHappensNext,
  ].join(' ');
  const words = text.split(/\s+/).filter((word) => word.length > 0).length;
  return Math.max(1, Math.ceil(words / WORDS_PER_MINUTE));
}

/** FR-11: no body and no user or organization data; the article title only from an article. */
export function supportMailto(articleTitle?: string): string {
  const subject =
    articleTitle === undefined ? SUPPORT_SUBJECT() : `${SUPPORT_SUBJECT()} – ${articleTitle}`;
  return `mailto:${environment.supportEmail}?subject=${encodeURIComponent(subject)}`;
}
