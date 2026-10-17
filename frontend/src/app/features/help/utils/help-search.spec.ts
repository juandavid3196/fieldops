import { HelpArticle, HelpArticleRef, HelpTopic } from '../models/help.model';
import { allArticles, topicsFor } from './help-content';
import { normalizeText, searchArticles } from './help-search';

const article = (
  slug: string,
  title: string,
  overrides: Partial<HelpArticle> = {},
): HelpArticle => ({
  slug,
  title,
  description: 'Plain description',
  icon: 'pi-book',
  introduction: 'Plain introduction',
  steps: [{ text: 'Plain step' }],
  whatHappensNext: 'Plain next',
  ...overrides,
});

const topic = (slug: string, articles: HelpArticle[]): HelpTopic => ({
  slug,
  audience: 'internal',
  title: slug,
  icon: 'pi-book',
  cardSubtitle: 'card',
  headerSubtitle: 'header',
  sections: [{ label: 'SECTION', articles }],
  popular: [],
});

const alpha = topic('alpha', [
  article('void-an-invoice', 'Void an invoice'),
  article('record-a-response', 'Record a response', { description: 'Capture an approval' }),
  article('invite-your-team', 'Invite your team', {
    steps: [{ text: 'Open the permissions Matrix and choose a role' }],
  }),
]);
const beta = topic('beta', [
  article('bank-one', 'Bank transfer one', { callout: 'bank hint' }),
  article('bank-two', 'Bank transfer two'),
]);
const fixture: HelpArticleRef[] = allArticles([alpha, beta]);

const titles = (query: string): string[] =>
  searchArticles(fixture, query).map((result) => result.article.title);

describe('help search (BR-06, BR-13, AC-06, AC-11)', () => {
  it('matches every term case and diacritic insensitively across title, description and content, in catalog order', () => {
    expect(normalizeText('  Programación ÁÉÍ  ')).toBe('  programacion aei  ');

    expect(titles('void')).toEqual(['Void an invoice']);
    // Description-only term.
    expect(titles('capture')).toEqual(['Record a response']);
    // Term that only appears in a step, with mixed case and surrounding spaces.
    expect(titles('  MATRIX  ')).toEqual(['Invite your team']);
    // An accented query matches accent-free text.
    expect(titles('Matríx')).toEqual(['Invite your team']);
    // Two terms present, in any order; one absent term rejects the article.
    expect(titles('matrix permissions')).toEqual(['Invite your team']);
    expect(titles('permissions nonexistentterm')).toEqual([]);
    // Callout text is searchable; results keep catalog order across topics.
    expect(titles('bank')).toEqual(['Bank transfer one', 'Bank transfer two']);
    // Fewer than two characters: no filter, all articles in catalog order.
    expect(titles('v')).toHaveLength(5);
    expect(titles(' a ')).toEqual(fixture.map((result) => result.article.title));
  });

  it('scopes a topic search to that topic only', () => {
    const scoped = searchArticles(
      alpha.sections[0].articles.map((item) => ({ topic: alpha, article: item })),
      'a',
    );
    expect(scoped.every((result) => result.topic === alpha)).toBe(true);
    expect(searchArticles(fixture, 'transfer').every((result) => result.topic === beta)).toBe(true);
    expect(
      searchArticles(
        fixture.filter((result) => result.topic === alpha),
        'transfer',
      ),
    ).toEqual([]);
  });

  it('never returns articles of the other audience from the real catalog (BR-13, AC-22)', () => {
    const internal = allArticles(topicsFor('internal'));
    const portal = allArticles(topicsFor('portal'));

    // Every article title, searched inside its own audience, finds that article and only articles
    // of topics of that audience; the audiences share no article.
    for (const [own, audience] of [
      [internal, 'internal'],
      [portal, 'portal'],
    ] as const) {
      for (const { article: item } of own) {
        const found = searchArticles(own, item.title);
        expect(found.map((result) => result.article)).toContain(item);
        expect(found.every((result) => result.topic.audience === audience)).toBe(true);
      }
    }
    expect(
      internal.some((result) => portal.some((other) => other.article === result.article)),
    ).toBe(false);
  });
});
