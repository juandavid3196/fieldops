import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { topicsFor } from '../../utils/help-content';

const HELP_TOPICS = topicsFor('internal');

describe('help home', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  const host = () => harness.routeNativeElement as HTMLElement;
  const text = (selector: string) =>
    Array.from(host().querySelectorAll(selector), (node) => node.textContent?.trim());
  const settle = async () => {
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const search = async (value: string) => {
    const input = host().querySelector<HTMLInputElement>('#help-search')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'help', loadChildren: () => import('../../help.routes') }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/help');
    await settle();
  });

  afterEach(() => httpTesting.verify());

  it('renders the home layout, filters with search, shows no-match with the support card, and restores on clear (FR-03, FR-04, BR-07, AC-05 to AC-07, AC-16)', async () => {
    expect(text('app-help-breadcrumb li')).toEqual(['Help']);
    expect(host().querySelector('app-help-breadcrumb li')?.getAttribute('aria-current')).toBe(
      'page',
    );
    expect(host().querySelector('h1')?.textContent).toBe('Help & support');
    expect(host().textContent).toContain('Find answers and learn how to use FieldOps.');
    expect(host().querySelector('#help-search')?.getAttribute('placeholder')).toBe(
      'Search help articles...',
    );
    expect(host().querySelector('label[for="help-search"]')?.textContent).toContain(
      'Search help articles...',
    );
    const cards = Array.from(host().querySelectorAll<HTMLAnchorElement>('app-help-topic-card a'));
    expect(cards.map((card) => card.getAttribute('href'))).toEqual(
      HELP_TOPICS.map((topic) => `/help/${topic.slug}`),
    );
    expect(cards.map((card) => card.textContent?.replace(/\s+/g, ' ').trim())).toEqual(
      HELP_TOPICS.map((topic) => `${topic.title}${topic.cardSubtitle}`),
    );
    expect(text('h2')).toEqual(['Browse by topic', 'Popular questions', 'Still need help?']);
    expect(host().textContent).toContain('Support is provided by email.');
    const contact = host().querySelector<HTMLAnchorElement>('a.support__action')!;
    expect(contact.getAttribute('href')).toBe(
      'mailto:support@fieldops.app?subject=FieldOps%20support%20request',
    );

    await search('Invite your team');
    expect(host().querySelector('app-help-topic-card')).toBeNull();
    expect(text('app-help-result-list .results__title')).toContain('Invite your team');
    expect(text('app-help-result-list .results__topic')).toContain('Getting started');
    expect(host().querySelector('[role="status"]')?.textContent?.trim()).toMatch(
      /^\d+ articles? found$/,
    );

    await search('permission nonexistentterm');
    expect(host().querySelector('.home__empty')?.textContent?.trim()).toBe(
      'No articles match "permission nonexistentterm"',
    );
    expect(host().querySelector('app-help-support-card')).not.toBeNull();

    await search('');
    expect(host().querySelectorAll('app-help-topic-card')).toHaveLength(7);
    expect(host().querySelector('.home__empty')).toBeNull();
    expect(host().querySelector('[role="status"]')?.textContent?.trim()).toBe('');
  });

  it('keeps several popular questions open with their article links and lists every article grouped by topic with view=all (FR-05, FR-06, AC-08, AC-09)', async () => {
    const headers = Array.from(host().querySelectorAll<HTMLElement>('p-accordion-header'));
    expect(headers.map((button) => button.textContent?.trim())).toEqual([
      'How do I create a service request?',
      'How do I turn an approved quote into a job?',
      'How do I assign a technician?',
      'How do I record a payment?',
    ]);

    headers[0].click();
    await settle();
    headers[3].click();
    await settle();
    expect(headers.map((button) => button.getAttribute('aria-expanded'))).toEqual([
      'true',
      'false',
      'false',
      'true',
    ]);
    const links = Array.from(host().querySelectorAll<HTMLAnchorElement>('a.home__read'));
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/help/requests-quotes-jobs/create-a-service-request',
      '/help/requests-quotes-jobs/create-a-job-from-a-quote',
      '/help/scheduling-team/assign-a-technician',
      '/help/invoices-payments/record-a-payment',
    ]);

    host().querySelector<HTMLAnchorElement>('a.support__all')!.click();
    await settle();
    expect(router.url).toBe('/help?view=all');
    expect(host().querySelector('h1')?.textContent).toBe('All help articles');
    expect(host().querySelector('.home__back')?.textContent).toContain('Back to Help & support');
    expect(text('.home__group-title')).toEqual(HELP_TOPICS.map((topic) => topic.title));
    expect(host().querySelectorAll('app-help-result-list li')).toHaveLength(46);
    expect(host().querySelector('app-help-topic-card')).toBeNull();
  });
});
