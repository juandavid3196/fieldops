import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../core/config/api.config';
import { GlobalSearchEntityType, GlobalSearchItem } from '../../../core/models/global-search.model';
import { GlobalSearch } from './global-search';

const URL = 'http://api.test/search/global';

function item(
  type: GlobalSearchEntityType,
  id: string,
  over: Partial<GlobalSearchItem> = {},
): GlobalSearchItem {
  return {
    entityType: type,
    entityId: id,
    displayNumber: null,
    title: `Title ${id}`,
    subtitle: null,
    status: null,
    branchId: null,
    branchName: null,
    updatedAt: '2026-01-01T00:00:00Z',
    ...over,
  };
}

describe('GlobalSearch', () => {
  let fixture: ComponentFixture<GlobalSearch>;
  let http: HttpTestingController;
  let router: Router;
  let navigate: ReturnType<typeof vi.spyOn>;
  let host: HTMLElement;
  let input: HTMLInputElement;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(GlobalSearch);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    input = host.querySelector('input')!;
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const render = () => fixture.detectChanges();
  const text = () => host.querySelector('.gs__panel')?.textContent?.replace(/\s+/g, ' ') ?? '';
  const options = () => Array.from(host.querySelectorAll<HTMLElement>('[role="option"]'));
  const key = (name: string, init: KeyboardEventInit = {}) => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, ...init }));
    render();
  };

  async function type(value: string, wait = 0): Promise<void> {
    input.focus();
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    render();
    await vi.advanceTimersByTimeAsync(wait);
    render();
  }

  async function search(value: string, items: GlobalSearchItem[], hasMore = false) {
    await type(value, 300);
    http.expectOne((r) => r.url === URL).flush({ query: value.trim(), items, hasMore });
    render();
  }

  it('sends no request under 2 characters, debounces 300 ms with the trimmed value and cancels superseded requests (FR-06, AC-01, AC-08)', async () => {
    await type('   ', 400);
    expect(text()).toContain('Search by name, number, email, or phone.');
    await type(' a ', 400);
    expect(text()).toContain('Enter at least 2 characters.');
    http.expectNone((r) => r.url === URL);

    await type('ab', 100);
    await type(' abc ', 299);
    http.expectNone((r) => r.url === URL);
    expect(host.querySelectorAll('.gs__skeleton')).toHaveLength(4);
    await vi.advanceTimersByTimeAsync(1);
    const first: TestRequest = http.expectOne((r) => r.url === URL);
    expect(first.request.params.get('q')).toBe('abc');
    expect(first.request.params.get('limit')).toBe('20');

    await type('abcd', 300);
    expect(first.cancelled).toBe(true);
    const latest = http.expectOne((r) => r.url === URL);
    latest.flush({ query: 'abcd', items: [], hasMore: false });
    render();
    expect(options()).toHaveLength(0);
    expect(text()).toContain('No results for “abcd”.');
  });

  it('shows the error state with Retry that resends immediately, and keeps the panel open when focus moves into it (FR-07, FR-09, AC-05, AC-09)', async () => {
    await type('boiler', 300);
    http.expectOne((r) => r.url === URL).flush(null, { status: 500, statusText: 'Server' });
    render();
    expect(text()).toContain("Couldn't search right now.");

    const retry = host.querySelector<HTMLButtonElement>('.gs__error button')!;
    retry.focus();
    input.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: retry }));
    render();
    expect(input.getAttribute('aria-expanded')).toBe('true');

    retry.click();
    render();
    await vi.advanceTimersByTimeAsync(0);
    expect(host.querySelectorAll('.gs__skeleton')).toHaveLength(4);
    http
      .expectOne((r) => r.url === URL && r.params.get('q') === 'boiler')
      .flush({ query: 'boiler', items: [item('customer', 'c1')], hasMore: false });
    render();
    expect(options()).toHaveLength(1);
  });

  it('renders grouped rows in response order with number, subtitle, branch, status chip and more-results note (FR-07, AC-16)', async () => {
    await search(
      'mart',
      [
        item('customer', 'c1', {
          title: 'Sofia Martinez',
          subtitle: 'Residential',
          status: 'active',
        }),
        item('service_request', 'r1', {
          displayNumber: 'SR-1',
          title: 'Faucet',
          subtitle: 'Sofia',
          branchName: 'North',
          status: 'needs_review',
        }),
        item('customer', 'c2', { title: 'Old Co', status: 'inactive' }),
        item('invoice', 'i1', { displayNumber: 'INV-1', title: '$5.00', status: 'paid' }),
      ],
      true,
    );

    const groups = Array.from(host.querySelectorAll('[role="group"]'));
    expect(groups.map((g) => g.querySelector('.gs__group')?.textContent)).toEqual([
      'Customers',
      'Requests',
      'Invoices',
    ]);
    expect(groups[0].getAttribute('aria-labelledby')).toBe(
      groups[0].querySelector('.gs__group')!.id,
    );
    expect(host.querySelector('.gs__title')?.textContent).toBe('Search results');
    expect(host.querySelector('.gs__count')?.textContent).toBe('4 results');
    expect(options()[2].querySelector('.gs__line')?.textContent).toBe('SR-1 · Faucet');
    expect(options()[2].querySelector('.gs__meta')?.textContent).toBe('Sofia · North');
    expect(options().map((o) => o.querySelector('.gs__chip')?.textContent)).toEqual([
      'Customer',
      'Inactive',
      'Needs review',
      'Paid',
    ]);
    expect(options()[0].getAttribute('aria-label')).toBe(
      'Customer, Sofia Martinez, Residential, Customer',
    );
    expect(text()).toContain('More results exist. Refine your search.');
    expect(text()).toContain('↑↓ Navigate');
    expect(text()).toContain('Enter Open');
    expect(text()).toContain('Esc Close');
  });

  it.each([
    ['customer', '/customers/id-1'],
    ['service_request', '/requests?request=id-1'],
    ['quote', '/quotes/id-1'],
    ['work_order', '/jobs/id-1'],
    ['invoice', '/invoices/id-1'],
  ] as const)(
    'opens %s on click, clearing the field and closing (FR-08, AC-04)',
    async (type_, url) => {
      await search('abc', [item(type_, 'id-1')]);

      options()[0].click();
      render();

      expect(navigate).toHaveBeenCalledWith(url);
      expect(input.value).toBe('');
      expect(host.querySelector('.gs__panel')).toBeNull();
    },
  );

  it('supports Ctrl/Cmd+K, arrow wrap, Enter, Escape and outside click with combobox ARIA (FR-09, AC-05, AC-06, AC-17)', async () => {
    key('Enter');
    expect(navigate).not.toHaveBeenCalled();

    input.value = 'abc';
    const prevented = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, cancelable: true });
    document.dispatchEvent(prevented);
    expect(prevented.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(input);
    expect(input.selectionEnd).toBe(3);
    expect(input.maxLength).toBe(100);

    await search('abc', [item('quote', 'a'), item('quote', 'b'), item('quote', 'c')]);
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.getAttribute('aria-controls')).toBe(host.querySelector('[role="listbox"]')!.id);
    expect(input.getAttribute('aria-activedescendant')).toBe(options()[0].id);

    key('ArrowUp');
    expect(input.getAttribute('aria-activedescendant')).toBe(options()[2].id);
    key('ArrowDown');
    expect(input.getAttribute('aria-activedescendant')).toBe(options()[0].id);
    key('ArrowDown');
    key('Enter');
    expect(navigate).toHaveBeenCalledWith('/quotes/b');

    await search('xyz', [item('quote', 'a')]);
    key('Escape');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(input);
    expect(input.value).toBe('xyz');

    key('ArrowDown');
    expect(input.getAttribute('aria-expanded')).toBe('true');
    document.body.click();
    render();
    expect(host.querySelector('.gs__panel')).toBeNull();
    expect(router.url).toBe('/');
  });
});
