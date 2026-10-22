import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { API_CONFIG } from '../config/api.config';
import { PortalSessionService } from '../services/portal-session.service';
import { SessionService } from '../services/session.service';
import { effectiveMode, resolvePreference } from './theme-resolver';
import { THEME_STORAGE_KEY } from './theme.config';
import { ThemeService } from './theme.service';

const API = 'http://api.test';

const isDark = () => document.documentElement.classList.contains('app-dark');
const themeColor = () =>
  document.querySelector('meta[name="theme-color"]')?.getAttribute('content') ?? null;

beforeEach(() => {
  const meta = document.createElement('meta');
  meta.name = 'theme-color';
  meta.content = '#0f9d9a';
  document.head.appendChild(meta);
});

afterEach(() => {
  document.querySelector('meta[name="theme-color"]')?.remove();
  vi.restoreAllMocks();
});

/** Rows of AC-01: shared by the pre-bootstrap script and the application resolver. */
const RESOLUTION_TABLE: readonly {
  name: string;
  local: string | null | 'throws';
  device: boolean | 'unavailable';
  mode: 'light' | 'dark';
}[] = [
  { name: 'local dark, device light', local: 'dark', device: false, mode: 'dark' },
  { name: 'local light, device dark', local: 'light', device: true, mode: 'light' },
  { name: 'local system, device dark', local: 'system', device: true, mode: 'light' },
  { name: 'no local, device dark', local: null, device: true, mode: 'light' },
  { name: 'local sepia, device dark', local: 'sepia', device: true, mode: 'light' },
  { name: 'local sepia, device light', local: 'sepia', device: false, mode: 'light' },
  { name: 'local system, device light', local: 'system', device: false, mode: 'light' },
  { name: 'no local, no matchMedia', local: null, device: 'unavailable', mode: 'light' },
  { name: 'storage throws, device dark', local: 'throws', device: true, mode: 'light' },
];

describe('first-paint resolution (FR-01, BR-02, BR-04, AC-01)', () => {
  it('runs the index.html script and the application resolver over the same table', async () => {
    const moduleName = 'node:fs';
    const fs = (await import(/* @vite-ignore */ moduleName)) as {
      readFileSync(path: string, encoding: string): string;
    };
    const html = fs.readFileSync('src/index.html', 'utf8');
    const script = /<script>([\s\S]*?)<\/script>/.exec(html)?.[1];
    expect(script).toBeDefined();

    for (const row of RESOLUTION_TABLE) {
      const root = document.implementation.createHTMLDocument('x');
      const meta = root.createElement('meta');
      meta.name = 'theme-color';
      meta.content = '#0f9d9a';
      root.head.appendChild(meta);
      const fakeWindow = {
        localStorage: {
          getItem: () => {
            if (row.local === 'throws') throw new Error('blocked');
            return row.local;
          },
        },
        matchMedia:
          row.device === 'unavailable'
            ? undefined
            : () => ({ matches: row.device === true }) as MediaQueryList,
      };
      new Function('window', 'document', script ?? '')(fakeWindow, root);

      const local = row.local === 'throws' ? null : row.local;
      const resolved = effectiveMode(resolvePreference(null, local));
      const expected = row.mode;
      expect(resolved, row.name).toBe(expected);
      expect(root.documentElement.classList.contains('app-dark'), row.name).toBe(
        expected === 'dark',
      );
      expect(root.documentElement.style.colorScheme, row.name).toBe(expected);
      expect(meta.content, row.name).toBe(expected === 'dark' ? '#0b1620' : '#0f9d9a');
    }
  });
});

describe('session precedence and system as light (FR-02, BR-02, BR-03, AC-02)', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { baseUrl: API } },
      ],
    });
    return {
      http: TestBed.inject(HttpTestingController),
      theme: TestBed.inject(ThemeService),
      internal: TestBed.inject(SessionService),
      portal: TestBed.inject(PortalSessionService),
    };
  }
  const user = { id: 'u', firstName: 'A', lastName: 'B', email: 'a@b.c', photoUpdatedAt: null };

  it.each(['internal', 'portal'] as const)(
    'applies the %s session value over the local one and ignores an unsupported value without requests',
    async (surface) => {
      const { http, theme, internal, portal } = setup();
      localStorage.setItem(THEME_STORAGE_KEY, 'dark');
      theme.init();
      expect(isDark()).toBe(true);

      const load = async (preferredTheme: string) => {
        if (surface === 'internal') {
          const done = firstValueFrom(internal.loadCurrent());
          http.expectOne(`${API}/sessions/current`).flush({
            user: { ...user, preferredTheme },
            organization: { id: 'o', name: 'Org' },
            role: { code: 'owner', name: 'Owner' },
          });
          await done;
        } else {
          const done = firstValueFrom(portal.loadCurrent());
          http.expectOne(`${API}/portal/sessions/current`).flush({
            user: { firstName: 'A', lastName: 'B', email: 'a@b.c', preferredTheme },
            account: { contactId: 'c', customerName: 'C', organizationName: 'O', hasLogo: false },
            accounts: [],
          });
          await done;
        }
      };

      await load('light');
      expect(isDark()).toBe(false);
      expect(document.documentElement.style.colorScheme).toBe('light');
      expect(themeColor()).toBe('#0f9d9a');
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');

      await load('system');
      expect(isDark()).toBe(false);
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
      expect(themeColor()).toBe('#0f9d9a');

      await load('sepia');
      expect(theme.preference()).toBe('system');
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
      http.verify();
    },
  );

  it('renders system as light even on a dark device and survives unavailable storage', () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: vi.fn(() => ({ matches: true })),
    });
    const { theme } = setup();
    theme.init();
    expect(isDark()).toBe(false);
    expect(themeColor()).toBe('#0f9d9a');
    theme.apply('dark');
    expect(isDark()).toBe(true);
    theme.apply('system');
    expect(isDark()).toBe(false);

    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() => theme.commit('dark')).not.toThrow();
    expect(isDark()).toBe(true);
    expect(theme.persisted()).toBe('dark');
  });
});
