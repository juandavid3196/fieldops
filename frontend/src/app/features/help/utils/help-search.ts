import { HelpArticle, HelpArticleRef } from '../models/help.model';

const MIN_QUERY_LENGTH = 2;

/** Lower case without diacritics (BR-06). */
export function normalizeText(value: string): string {
  return value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

/** The trimmed query when it is long enough to filter; `null` shows the default view (BR-06). */
export function effectiveQuery(query: string): string | null {
  const trimmed = query.trim();
  return trimmed.length >= MIN_QUERY_LENGTH ? trimmed : null;
}

function searchableText(article: HelpArticle): string {
  return normalizeText(
    [
      article.title,
      article.description,
      article.introduction,
      article.callout ?? '',
      ...article.steps.map((step) => step.text),
      article.whatHappensNext,
    ].join(' '),
  );
}

/**
 * BR-06: every whitespace-separated term must appear in the article text. The input order (catalog
 * order) is kept. Queries under two characters apply no filter and return every article.
 */
export function searchArticles(
  articles: readonly HelpArticleRef[],
  query: string,
): HelpArticleRef[] {
  const effective = effectiveQuery(query);
  if (effective === null) {
    return [...articles];
  }
  const terms = normalizeText(effective).split(/\s+/);
  return articles.filter(({ article }) => {
    const text = searchableText(article);
    return terms.every((term) => text.includes(term));
  });
}
