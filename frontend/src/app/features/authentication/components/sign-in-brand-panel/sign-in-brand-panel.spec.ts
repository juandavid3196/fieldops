import { TestBed } from '@angular/core/testing';

import { SignInBrandPanel } from './sign-in-brand-panel';

describe('SignInBrandPanel', () => {
  let element: HTMLElement;

  beforeEach(async () => {
    const fixture = TestBed.createComponent(SignInBrandPanel);
    await fixture.whenStable();
    element = fixture.nativeElement as HTMLElement;
  });

  const texts = (selector: string) =>
    Array.from(element.querySelectorAll(selector), (node) => node.textContent?.trim());

  it('is a complementary landmark labelled by its h2', () => {
    const aside = element.querySelector('aside');
    expect(aside?.getAttribute('aria-labelledby')).toBe('brand-heading');
    const heading = element.querySelector('#brand-heading');
    expect(heading?.tagName).toBe('H2');
    expect(heading?.textContent).toContain('Run every service job from one place');
    expect(element.querySelector('h1')).toBeNull();
  });

  it('shows the text wordmark, tagline and subhead', () => {
    expect(element.querySelector('.brand__wordmark')?.textContent?.trim()).toBe('FieldOps');
    expect(element.textContent).toContain('People · Work · Customers · For a brighter tomorrow');
    expect(element.textContent).toContain(
      'Schedule, dispatch, track field work, delight customers, and get paid — all in FieldOps.',
    );
  });

  it('shows the three status cards', () => {
    expect(texts('.brand__card-title')).toEqual([
      'Request approved',
      'Technician assigned',
      'Invoice paid',
    ]);
    expect(texts('.brand__card-text')).toEqual([
      'Customer request in, ready to schedule.',
      'The right person for the job.',
      'Work complete. Payment received.',
    ]);
  });

  it('shows the three benefits as text only', () => {
    expect(texts('.brand__benefit-title')).toEqual([
      'Work runs smoother',
      'Happier customers',
      'A more profitable business',
    ]);
    expect(texts('.brand__benefit-text')).toEqual([
      'From requests to payments, all in one place.',
      'Faster response times and clear communication.',
      'Less admin work. More time for what matters.',
    ]);
    expect(element.querySelector('svg, i')).toBeNull();
  });

  it('renders the background photo as a decorative, lazy image', () => {
    const images = element.querySelectorAll('img');
    expect(images).toHaveLength(1);
    expect(images[0].getAttribute('alt')).toBe('');
    expect(images[0].getAttribute('src')).toBe('/images/signin-background.jpg');
    expect(images[0].getAttribute('loading')).toBe('lazy');
  });
});
