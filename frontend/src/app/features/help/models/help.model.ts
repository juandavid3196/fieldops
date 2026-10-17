/** Optional screenshot of a step; `alt` is required and non-empty (BR-11). */
export interface HelpScreenshot {
  readonly src: string;
  readonly alt: string;
}

export interface HelpStep {
  readonly text: string;
  readonly screenshot?: HelpScreenshot;
}

export interface HelpArticle {
  /** Kebab-case form of the title, unique within its topic (BR-04). */
  readonly slug: string;
  readonly title: string;
  readonly description: string;
  /** PrimeIcons class shown in topic-page lists. */
  readonly icon: string;
  readonly introduction: string;
  readonly callout?: string;
  readonly steps: readonly HelpStep[];
  readonly whatHappensNext: string;
}

export interface HelpSection {
  /** Uppercase label exactly as in the catalog (BR-04). */
  readonly label: string;
  readonly articles: readonly HelpArticle[];
}

/** BR-13: `/help/**` resolves internal topics only, `/portal/help/**` portal topics only. */
export type HelpAudience = 'internal' | 'portal';

export interface HelpTopic {
  readonly slug: string;
  readonly title: string;
  readonly audience: HelpAudience;
  readonly icon: string;
  /** Card subtitle on the home page and in Related topics (BR-03). */
  readonly cardSubtitle: string;
  /** Header subtitle on the topic page (BR-03). */
  readonly headerSubtitle: string;
  readonly sections: readonly HelpSection[];
  /** Article slugs in the order of Popular in this topic (three entries). */
  readonly popular: readonly string[];
}

export interface HelpPopularQuestion {
  readonly question: string;
  /** One or two sentences. */
  readonly answer: string;
  readonly topicSlug: string;
  readonly articleSlug: string;
}

/** An article with the topic it belongs to, used by search and result lists. */
export interface HelpArticleRef {
  readonly topic: HelpTopic;
  readonly article: HelpArticle;
}
