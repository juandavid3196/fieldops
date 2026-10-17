import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../../app.routes';
import { portalGuard } from '../../core/guards/portal.guard';
import { authGuard } from '../../core/guards/auth.guard';
import portalRoutes from '../portal/portal.routes';
import { HELP_TOPICS } from './data/help-catalog';
import { HelpArticle, HelpAudience } from './models/help.model';
import { helpBasePath, topicArticles } from './utils/help-content';

const basePath = helpBasePath;
const titleSuffix = (audience: HelpAudience): string =>
  audience === 'portal' ? 'Help & support · Customer portal' : 'Help & support · FieldOps';

describe('help topic and article routes', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  const host = () => harness.routeNativeElement as HTMLElement;
  const text = (selector: string) =>
    Array.from(host().querySelectorAll(selector), (node) =>
      node.textContent?.replace(/\s+/g, ' ').trim(),
    );
  const settle = async () => {
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const go = async (url: string) => {
    await harness.navigateByUrl(url);
    await settle();
  };
  /** FR-11 / FR-17: internal mailto; portal provider card with no mailto and no email note. */
  const expectSupport = (audience: HelpAudience, internalSubject: string) => {
    const action = host().querySelector<HTMLAnchorElement>('a.support__action')!;
    if (audience === 'portal') {
      expect(host().querySelector('.support__text')?.textContent).toBe(
        'Contact your service provider.',
      );
      expect(action.textContent?.trim()).toBe('Get help from your provider');
      expect(action.getAttribute('href')).toBe('/portal#help');
      expect(host().querySelector('a[href^="mailto:"]')).toBeNull();
      expect(host().textContent).not.toContain('Support is provided by email.');
    } else {
      expect(action.getAttribute('href')).toBe(
        `mailto:support@fieldops.app?subject=${internalSubject}`,
      );
    }
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'help', loadChildren: () => import('./help.routes') },
          { path: 'portal/help', loadChildren: () => import('./portal-help.routes') },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  it.each(HELP_TOPICS.map((topic) => [topic.slug, topic] as const))(
    'renders the %s topic page with sections, popular entries, support and related topics, and scopes its search (FR-07, FR-08, FR-16, FR-17, AC-10, AC-11, AC-16, AC-22, AC-24)',
    async (_slug, topic) => {
      const base = basePath(topic.audience);
      await go(`${base}/${topic.slug}`);

      expect(text('app-help-breadcrumb li')).toEqual(['Help', topic.title]);
      expect(document.title).toBe(`${topic.title} · ${titleSuffix(topic.audience)}`);
      expect(host().querySelector('h1')?.textContent).toBe(topic.title);
      expect(host().textContent).toContain(topic.headerSubtitle);
      expect(host().querySelector('.topic__back')?.textContent).toContain('Back to Help & support');
      expect(host().querySelector('input')?.getAttribute('placeholder')).toBe(
        `Search in ${topic.title}...`,
      );
      expect(text('.topic__section-label')).toEqual(topic.sections.map((section) => section.label));
      expect(text('.topic__list .topic__item-title')).toEqual(
        topic.sections.flatMap((section) => section.articles.map((article) => article.title)),
      );
      expect(text('.topic__popular-item')).toHaveLength(3);
      expect(text('.topic__number')).toEqual(['1', '2', '3']);
      expect(text('.topic__popular-link')).toEqual(
        topic.popular.map((slug) => topicArticles(topic).find((a) => a.slug === slug)?.title),
      );
      expectSupport(topic.audience, 'FieldOps%20support%20request');
      expect(host().querySelector('.topic__back')?.getAttribute('href')).toBe(base);
      expect(host().querySelector('app-help-breadcrumb a')?.getAttribute('href')).toBe(base);
      expect(
        Array.from(host().querySelectorAll('.topic__item, .topic__popular-link')).every((link) =>
          link.getAttribute('href')?.startsWith(`${base}/${topic.slug}/`),
        ),
      ).toBe(true);
      // Related topics: the other topics of the same audience, linked inside the base path.
      const related = HELP_TOPICS.filter(
        (other) => other !== topic && other.audience === topic.audience,
      );
      expect(related).toHaveLength(topic.audience === 'portal' ? 2 : 6);
      expect(text('app-help-topic-card .topic__title')).toEqual(
        related.map((other) => other.title),
      );
      expect(
        Array.from(host().querySelectorAll('app-help-topic-card a'), (a) => a.getAttribute('href')),
      ).toEqual(related.map((other) => `${base}/${other.slug}`));

      const first = topic.sections[0].articles[0];
      const input = host().querySelector<HTMLInputElement>('#help-topic-search')!;
      input.value = first.title;
      input.dispatchEvent(new Event('input'));
      await settle();
      expect(text('.topic__item-title')).toContain(first.title);
      expect(host().querySelectorAll('.topic__section-label').length).toBeLessThan(
        topic.sections.length + 1,
      );

      input.value = 'nonexistentterm';
      input.dispatchEvent(new Event('input'));
      await settle();
      expect(host().querySelector('.topic__empty')?.textContent).toBe(
        `No articles in ${topic.title} match "nonexistentterm"`,
      );
      expect(host().querySelector('.topic__section-label')).toBeNull();
    },
  );

  it('renders the article template with optional parts, previous/next boundaries and in-memory feedback (FR-09, FR-10, AC-12 to AC-16)', async () => {
    const topic = HELP_TOPICS[0];
    const [first, , middle, , , last] = topicArticles(topic);

    await go(`/help/${topic.slug}/${first.slug}`);
    expect(text('app-help-breadcrumb li')).toEqual(['Help', topic.title, first.title]);
    expect(
      host().querySelector('app-help-breadcrumb [aria-current="page"]')?.textContent?.trim(),
    ).toBe(first.title);
    expect(host().querySelectorAll('app-help-breadcrumb a')).toHaveLength(2);
    expect(host().querySelector('.article__back')?.textContent).toContain(`Back to ${topic.title}`);
    expect(host().querySelector('h1')?.textContent).toBe(first.title);
    expect(host().querySelector('.article__time')?.textContent).toMatch(
      /^\s*[1-9]\d* min read\s*$/,
    );
    expect(host().querySelectorAll('ol.article__steps > li')).toHaveLength(first.steps.length);
    expect(host().querySelector('h2#article-next-title')?.textContent).toBe('What happens next?');
    expect(host().querySelector('.article__callout')).not.toBeNull();
    expect(host().querySelector('img')).toBeNull();
    // First article: only Next, with the target title.
    expect(text('.article__pager-link')).toEqual([`Next${topicArticles(topic)[1].title}`]);
    expect(host().querySelector('a.support__action')?.getAttribute('href')).toBe(
      `mailto:support@fieldops.app?subject=FieldOps%20support%20request%20%E2%80%93%20${encodeURIComponent(first.title)}`,
    );

    // Feedback: Yes replaces the question; no request is sent.
    expect(host().querySelector('.article__question')?.textContent).toBe(
      'Was this article helpful?',
    );
    const buttons = Array.from(
      host().querySelectorAll<HTMLButtonElement>('.article__answers button'),
    );
    expect(buttons.map((button) => button.textContent?.trim())).toEqual(['Yes', 'No']);
    buttons[1].click();
    await settle();
    expect(host().querySelector('.article__thanks')?.textContent).toBe('Thanks for your feedback.');
    expect(host().querySelector('.article__answers')).toBeNull();
    expect(host().querySelector('.article__feedback')?.getAttribute('aria-live')).toBe('polite');

    // Another article shows the question again; middle has both links, last only Previous.
    await go(`/help/${topic.slug}/${middle.slug}`);
    expect(host().querySelector('.article__question')).not.toBeNull();
    expect(host().querySelector('.article__thanks')).toBeNull();
    expect(text('.article__pager-link')).toEqual([
      `Previous${topicArticles(topic)[1].title}`,
      `Next${topicArticles(topic)[3].title}`,
    ]);
    await go(`/help/${topic.slug}/${last.slug}`);
    expect(text('.article__pager-link')).toEqual([`Previous${topicArticles(topic)[4].title}`]);

    // Optional callout and screenshot render only when present.
    const withParts: HelpArticle = {
      ...last,
      callout: undefined,
      steps: [
        { text: 'Plain step' },
        { text: 'Shot step', screenshot: { src: '/images/help/x.png', alt: 'The form' } },
      ],
    };
    const lastTopicArticles = topic.sections[1].articles;
    const original = lastTopicArticles.indexOf(last);
    (lastTopicArticles as HelpArticle[])[original] = withParts;
    await go(`/help/${topic.slug}/${first.slug}`);
    try {
      await go(`/help/${topic.slug}/${last.slug}`);
      expect(host().querySelector('.article__callout')).toBeNull();
      expect(host().querySelectorAll('img')).toHaveLength(1);
      expect(host().querySelector('img')?.getAttribute('alt')).toBe('The form');
      expect(host().querySelector('img')?.getAttribute('src')).toBe('/images/help/x.png');
    } finally {
      (lastTopicArticles as HelpArticle[])[original] = last;
    }
    expect(router.url).toBe(`/help/${topic.slug}/${last.slug}`);
  });

  it.each([
    ['unknown topic', '/help/unknown-topic', '/help'],
    ['unknown article', '/help/getting-started/unknown-article', '/help'],
    ['article under another topic', '/help/getting-started/record-a-payment', '/help'],
    ['portal topic under /help', '/help/portal-quotes-invoices', '/help'],
    ['internal topic under /portal/help', '/portal/help/getting-started', '/portal/help'],
    [
      'internal article under /portal/help',
      '/portal/help/requests-quotes-jobs/create-a-service-request',
      '/portal/help',
    ],
    ['portal article under /help', '/help/portal-quotes-invoices/pay-an-invoice-online', '/help'],
    ['unknown portal topic', '/portal/help/unknown-topic', '/portal/help'],
  ])(
    'shows Article not found with a way back for %s (FR-12, FR-16, AC-17, AC-23)',
    async (_case, url, base) => {
      await go(url);

      expect(host().querySelector('h1')?.textContent).toBe('Article not found');
      const back = host().querySelector<HTMLAnchorElement>('a.missing__back')!;
      expect(back.textContent).toContain('Back to Help & support');
      expect(back.getAttribute('href')).toBe(base);
    },
  );

  it('serves the portal home and article inside /portal/help with portal topics, questions and the provider card (FR-16, FR-17, BR-13, AC-22, AC-24)', async () => {
    const portalTopics = HELP_TOPICS.filter((topic) => topic.audience === 'portal');

    await go('/portal/help');
    expect(document.title).toBe('Help & support · Customer portal');
    expect(
      Array.from(host().querySelectorAll('app-help-topic-card a'), (a) => a.getAttribute('href')),
    ).toEqual(portalTopics.map((topic) => `/portal/help/${topic.slug}`));
    expect(
      Array.from(host().querySelectorAll('p-accordion-header'), (h) => h.textContent?.trim()),
    ).toEqual([
      'How do I request a service?',
      'How do I approve a quote?',
      'How do I pay an invoice?',
      'How do I reschedule an appointment?',
    ]);
    expectSupport('portal', '');
    expect(host().querySelector('a.support__all')?.getAttribute('href')).toBe(
      '/portal/help?view=all',
    );

    // Portal search never returns internal articles.
    const input = host().querySelector<HTMLInputElement>('#help-search')!;
    input.value = 'Create a service request';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(
      text('app-help-result-list .results__topic').every((title) =>
        portalTopics.some((topic) => topic.title === title),
      ),
    ).toBe(true);

    await go('/portal/help?view=all');
    expect(host().querySelector('h1')?.textContent).toBe('All help articles');
    expect(host().querySelector('.home__back')?.getAttribute('href')).toBe('/portal/help');
    expect(text('.home__group-title')).toEqual(portalTopics.map((topic) => topic.title));
    expect(host().querySelectorAll('app-help-result-list li')).toHaveLength(15);
    expect(
      Array.from(host().querySelectorAll('app-help-result-list a'), (a) => a.getAttribute('href')),
    ).toEqual(
      portalTopics.flatMap((topic) =>
        topicArticles(topic).map((article) => `/portal/help/${topic.slug}/${article.slug}`),
      ),
    );

    const topic = portalTopics[0];
    const [first, second] = topicArticles(topic);
    await go(`/portal/help/${topic.slug}/${first.slug}`);
    expect(document.title).toBe(`${first.title} · Help & support · Customer portal`);
    expect(
      Array.from(host().querySelectorAll('app-help-breadcrumb a'), (a) => a.getAttribute('href')),
    ).toEqual(['/portal/help', `/portal/help/${topic.slug}`]);
    expect(host().querySelector('.article__back')?.getAttribute('href')).toBe(
      `/portal/help/${topic.slug}`,
    );
    expect(host().querySelector('.article__pager-link')?.getAttribute('href')).toBe(
      `/portal/help/${topic.slug}/${second.slug}`,
    );
    expectSupport('portal', '');
  });

  it('places /help under the authenticated shell and /portal/help under the guarded portal shell (BR-01, AC-04, AC-23)', () => {
    const appShell = routes.find(
      (route) => route.path === '' && route.canActivate?.includes(authGuard),
    );
    expect(appShell?.children?.some((route) => route.path === 'help')).toBe(true);
    expect(routes.some((route) => route.path === 'help')).toBe(false);

    const portalShell = portalRoutes.find(
      (route) => route.path === '' && route.canActivate?.includes(portalGuard),
    );
    const childPaths = (portalShell?.children ?? []).map((route) => route.path);
    expect(childPaths).toContain('help');
    expect(childPaths.indexOf('help')).toBeLessThan(childPaths.indexOf('**'));
    expect(portalRoutes.some((route) => route.path === 'help')).toBe(false);
  });
});
