import {
  HELP_POPULAR_QUESTIONS,
  HELP_PORTAL_POPULAR_QUESTIONS,
  HELP_TOPICS,
} from '../data/help-catalog';
import { HelpArticle } from '../models/help.model';
import {
  allArticles,
  findArticle,
  findTopic,
  helpBasePath,
  neighbours,
  popularQuestionsFor,
  readingMinutes,
  supportMailto,
  topicArticles,
  topicsFor,
} from './help-content';

const kebab = (title: string): string =>
  title
    .toLowerCase()
    .replace(/['’&]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');

/** BR-03, BR-04: topic slug, then each section with its article titles, in catalog order. */
const EXPECTED: Record<string, Record<string, string[]>> = {
  'getting-started': {
    'SET UP YOUR WORKSPACE': [
      'FieldOps basics',
      'Set up your organization',
      'Create and manage branches',
    ],
    'FIRST STEPS': [
      'Invite your team',
      'Add your first customer',
      'Configure products and services',
    ],
  },
  'requests-quotes-jobs': {
    'REQUESTS & QUOTES': [
      'Create a service request',
      'Build and send a quote',
      'Record a quote response',
      'Schedule an assessment',
    ],
    'JOBS & VISITS': ['Create a job from a quote', 'Add the first visit', 'Complete a job'],
  },
  'scheduling-team': {
    SCHEDULING: ['Schedule a visit', 'Assign a technician', 'Reschedule or cancel a visit'],
    'FIELD WORK': ['Update a visit status', 'Track time on a visit', 'Use the schedule calendar'],
  },
  'invoices-payments': {
    INVOICING: [
      'Create an invoice',
      'Review completed jobs before invoicing',
      'Review and send an invoice',
      'Report a bank transfer',
    ],
    PAYMENTS: ['Record a payment', 'Review overdue invoices', 'Void an invoice'],
  },
  'technician-field-app': {
    "TODAY'S JOBS": [
      "Use Today's jobs",
      'Open a job and read its details',
      'Use the technician menu',
    ],
    'ON SITE': [
      'Travel, arrive and start a job',
      'Complete tasks and add materials',
      'Add photos and notes',
      'Pause, resume or report an issue',
    ],
    'COMPLETE THE JOB': ['Review and complete a job'],
  },
  'dashboards-insights': {
    DASHBOARDS: [
      'Read the Operations Dashboard',
      'Read the Business Overview',
      'Use the Overview page',
    ],
    'REPORTS & ACTIVITY': [
      'Use the Reports workspace',
      'Review the Audit log',
      'Follow Notifications',
    ],
  },
  'account-administration': {
    'YOUR ACCOUNT': ['Update your profile', 'Change your password'],
    ADMINISTRATION: [
      'Manage users and permissions',
      'Manage technician skills and availability',
      'Manage customer records',
      'Invite a customer to the portal',
    ],
  },
  'portal-your-portal': {
    'GET STARTED': [
      'Activate your portal access',
      'Sign in and reset your password',
      'Get around your portal',
    ],
    'YOUR DETAILS': ['Manage your properties', 'Follow your updates and activity'],
  },
  'portal-requests-appointments': {
    REQUESTS: ['Request a service', 'Request a service without signing in', 'Follow your requests'],
    APPOINTMENTS: ['View your appointments', 'Request a reschedule'],
  },
  'portal-quotes-invoices': {
    QUOTES: ['Review and approve a quote', 'Ask a question or decline a quote'],
    INVOICES: ['View your invoices', 'Pay an invoice online', 'Report a bank transfer'],
  },
};

const POPULAR: Record<string, string[]> = {
  'getting-started': ['Add your first customer', 'Invite your team', 'Create and manage branches'],
  'requests-quotes-jobs': ['Create a job from a quote', 'Build and send a quote', 'Complete a job'],
  'scheduling-team': ['Assign a technician', 'Schedule a visit', 'Update a visit status'],
  'invoices-payments': ['Create an invoice', 'Record a payment', 'Review overdue invoices'],
  'technician-field-app': [
    'Travel, arrive and start a job',
    'Complete tasks and add materials',
    'Review and complete a job',
  ],
  'dashboards-insights': [
    'Read the Operations Dashboard',
    'Use the Reports workspace',
    'Read the Business Overview',
  ],
  'account-administration': [
    'Manage users and permissions',
    'Manage technician skills and availability',
    'Update your profile',
  ],
  'portal-your-portal': [
    'Activate your portal access',
    'Get around your portal',
    'Manage your properties',
  ],
  'portal-requests-appointments': [
    'Request a service',
    'View your appointments',
    'Request a reschedule',
  ],
  'portal-quotes-invoices': [
    'Review and approve a quote',
    'Pay an invoice online',
    'View your invoices',
  ],
};

describe('help catalog and content helpers', () => {
  it('has exactly the 10 topics and 61 articles of the catalog with audiences, valid references and complete content (FR-14, BR-03 to BR-05, BR-10, BR-11, BR-13, AC-18)', () => {
    expect(HELP_TOPICS.map((topic) => topic.slug)).toEqual(Object.keys(EXPECTED));
    expect(HELP_TOPICS.map((topic) => topic.audience)).toEqual([
      ...Array(7).fill('internal'),
      ...Array(3).fill('portal'),
    ]);
    expect([topicsFor('internal').length, topicsFor('portal').length]).toEqual([7, 3]);
    expect(allArticles(topicsFor('internal'))).toHaveLength(46);
    expect(allArticles(topicsFor('portal'))).toHaveLength(15);
    expect(allArticles()).toHaveLength(61);

    for (const topic of HELP_TOPICS) {
      const sections = Object.fromEntries(
        topic.sections.map((section) => [
          section.label,
          section.articles.map((article) => article.title),
        ]),
      );
      expect(sections).toEqual(EXPECTED[topic.slug]);
      expect(topic.icon).toMatch(/^pi-/);
      expect(topic.cardSubtitle).not.toBe('');
      expect(topic.headerSubtitle).not.toBe('');

      const articles = topicArticles(topic);
      const slugs = articles.map((article) => article.slug);
      expect(new Set(slugs).size).toBe(slugs.length);
      expect(topic.popular).toHaveLength(3);
      for (const slug of topic.popular) {
        expect(slugs).toContain(slug);
      }
      expect(topic.popular.map((slug) => articles.find((a) => a.slug === slug)?.title)).toEqual(
        POPULAR[topic.slug],
      );
    }

    for (const { article } of allArticles()) {
      expect(article.slug).toBe(kebab(article.title));
      expect(article.icon).toMatch(/^pi-/);
      expect(article.description.trim()).not.toBe('');
      expect(article.introduction.trim()).not.toBe('');
      expect(article.whatHappensNext.trim()).not.toBe('');
      expect(article.steps.length).toBeGreaterThanOrEqual(1);
      for (const step of article.steps) {
        expect(step.text.trim()).not.toBe('');
        if (step.screenshot) {
          expect(step.screenshot.alt.trim()).not.toBe('');
          expect(step.screenshot.src.trim()).not.toBe('');
        }
      }
    }
  });

  it('keeps popular questions inside their audience with valid targets and short answers (BR-05, BR-13)', () => {
    expect(popularQuestionsFor('internal')).toBe(HELP_POPULAR_QUESTIONS);
    expect(popularQuestionsFor('portal')).toBe(HELP_PORTAL_POPULAR_QUESTIONS);
    const targets = (audience: 'internal' | 'portal') =>
      popularQuestionsFor(audience).map((entry) => [
        entry.question,
        findArticle(entry.topicSlug, entry.articleSlug, audience)?.article.title,
      ]);

    expect(targets('internal')).toEqual([
      ['How do I create a service request?', 'Create a service request'],
      ['How do I turn an approved quote into a job?', 'Create a job from a quote'],
      ['How do I assign a technician?', 'Assign a technician'],
      ['How do I record a payment?', 'Record a payment'],
    ]);
    expect(targets('portal')).toEqual([
      ['How do I request a service?', 'Request a service'],
      ['How do I approve a quote?', 'Review and approve a quote'],
      ['How do I pay an invoice?', 'Pay an invoice online'],
      ['How do I reschedule an appointment?', 'Request a reschedule'],
    ]);
    for (const entry of [...HELP_POPULAR_QUESTIONS, ...HELP_PORTAL_POPULAR_QUESTIONS]) {
      expect(entry.answer.split(/(?<=[.!?])\s+/).length).toBeLessThanOrEqual(2);
    }
  });

  it('computes reading time, neighbours within a topic, audience lookups and the support mailto (BR-08, BR-09, BR-13, FR-11, AC-12, AC-14, AC-16, AC-23)', () => {
    const article = (words: number): HelpArticle => ({
      slug: 'x',
      title: 'X',
      description: 'ignored description words that never count',
      icon: 'pi-book',
      introduction: Array(words).fill('w').join(' '),
      steps: [{ text: 'one two' }],
      whatHappensNext: 'three',
    });
    // 3 extra words from the step and What happens next.
    expect([0, 197, 198, 397, 398].map((words) => readingMinutes(article(words)))).toEqual([
      1, 1, 2, 2, 3,
    ]);
    expect(readingMinutes({ ...article(197), callout: 'extra' })).toBe(2);

    const topic = HELP_TOPICS[0];
    const articles = topicArticles(topic);
    const first = neighbours(topic, articles[0]);
    const middle = neighbours(topic, articles[2]);
    const last = neighbours(topic, articles[5]);
    expect([first.previous, first.next?.slug]).toEqual([null, articles[1].slug]);
    expect([middle.previous?.slug, middle.next?.slug]).toEqual([
      articles[1].slug,
      articles[3].slug,
    ]);
    expect([last.previous?.slug, last.next]).toEqual([articles[4].slug, null]);

    expect(findArticle('getting-started', 'record-a-payment', 'internal')).toBeNull();
    expect(findArticle('unknown', 'record-a-payment', 'internal')).toBeNull();
    // Cross-audience slugs are not found, in both directions.
    expect(findTopic('getting-started', 'internal')?.title).toBe('Getting started');
    expect(findTopic('getting-started', 'portal')).toBeNull();
    expect(findTopic('portal-quotes-invoices', 'internal')).toBeNull();
    expect(findArticle('portal-quotes-invoices', 'pay-an-invoice-online', 'portal')).not.toBeNull();
    expect(findArticle('portal-quotes-invoices', 'pay-an-invoice-online', 'internal')).toBeNull();
    expect([helpBasePath('internal'), helpBasePath('portal')]).toEqual(['/help', '/portal/help']);

    expect(supportMailto()).toBe(
      'mailto:support@fieldops.app?subject=FieldOps%20support%20request',
    );
    expect(supportMailto('Record a payment')).toBe(
      'mailto:support@fieldops.app?subject=FieldOps%20support%20request%20%E2%80%93%20Record%20a%20payment',
    );
  });
});
