import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { LANGUAGE_STORAGE_KEY } from '../../../../core/i18n/languages.config';
import { LanguageService } from '../../../../core/i18n/language.service';
import { THEME_STORAGE_KEY } from '../../../../core/theme/theme.config';
import { ThemePreferenceService } from '../../../../core/theme/theme-preference.service';
import { ThemeService } from '../../../../core/theme/theme.service';
import { Preferences } from './preferences';

const API = 'http://api.test';

@Component({ template: '' })
class SignInStub {}

const isDark = () => document.documentElement.classList.contains('app-dark');

describe('Preferences page', () => {
  let fixture: ComponentFixture<Preferences>;
  let http: HttpTestingController;
  let language: LanguageService;
  let theme: ThemeService;

  const root = () => fixture.nativeElement as HTMLElement;
  const cards = () => Array.from(root().querySelectorAll<HTMLElement>('[role="radio"]'));
  const button = (label: string) =>
    Array.from(root().querySelectorAll<HTMLButtonElement>('.prefs__actions button')).find((b) =>
      b.textContent?.includes(label),
    )!;
  const tick = async (): Promise<void> => {
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  };
  const toasts = () => document.body.textContent ?? '';

  /** Saved `en` + `system`, as the session already loaded by the guard. */
  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'auth/sign-in', component: SignInStub }]),
        { provide: API_CONFIG, useValue: { baseUrl: API } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    language = TestBed.inject(LanguageService);
    theme = TestBed.inject(ThemeService);
    await firstValueFrom(language.init());
    theme.applyPreference('system');
    fixture = TestBed.createComponent(Preferences);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Selecting through the page API keeps the test independent of the PrimeNG overlay. */
  const selectLanguage = async (code: 'en' | 'es'): Promise<void> => {
    fixture.componentInstance.selectLanguage(code);
    await vi.waitFor(() => expect(language.lang()).toBe(code));
    fixture.detectChanges();
  };

  afterEach(() => {
    fixture?.destroy();
    document.querySelectorAll('app-preferences').forEach((element) => element.remove());
  });

  it('renders the design, previews without writing, resets and exposes the radio group by keyboard (FR-06, FR-07, BR-19, AC-06, AC-07, AC-08)', async () => {
    await create();

    expect(root().querySelectorAll('h2').length).toBe(2);
    expect(root().textContent).toContain('Display language');
    expect(root().textContent).toContain('Changes the language used across your account.');
    expect(root().textContent).toContain('Match your device settings for a seamless experience.');
    expect(root().textContent).toContain('Always use the light theme.');
    expect(root().textContent).toContain('Always use the dark theme.');
    expect(root().textContent).toContain('These preferences apply only to your user account.');
    expect(root().querySelector('.p-select-label')?.textContent).toContain('English');
    expect(button('Save preferences').disabled).toBe(true);
    expect(button('Reset').disabled).toBe(true);

    // One tab stop on the selected card; arrows move and select; Space selects.
    const group = root().querySelector('[role="radiogroup"]')!;
    expect(group.getAttribute('aria-label')).toBe('Appearance');
    expect(cards().map((c) => c.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false']);
    expect(cards().map((c) => c.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
    cards()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(cards().map((c) => c.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    expect(cards().map((c) => c.getAttribute('tabindex'))).toEqual(['-1', '0', '-1']);
    cards()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    expect(isDark()).toBe(true);
    cards()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    cards()[1].dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    cards()[0].dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    expect(cards()[0].getAttribute('aria-checked')).toBe('true');

    // Reselecting the saved values is pristine; a different pair is dirty with no request or local write.
    expect(button('Reset').disabled).toBe(true);
    cards()[2].click();
    await selectLanguage('es');
    expect(isDark()).toBe(true);
    expect(document.documentElement.lang).toBe('es');
    expect(button('Guardar preferencias').disabled).toBe(false);
    expect(button('Restablecer').disabled).toBe(false);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');

    button('Restablecer').click();
    await vi.waitFor(() => expect(language.lang()).toBe('en'));
    fixture.detectChanges();
    expect(isDark()).toBe(false);
    expect(button('Save preferences').disabled).toBe(true);
    expect(button('Reset').disabled).toBe(true);
    cards()[2].click();
    fixture.detectChanges();
    cards()[0].click();
    fixture.detectChanges();
    expect(button('Save preferences').disabled).toBe(true);
    http.verify();
  });

  it.each([
    { name: 'language only', code: 'es', value: 'system' },
    { name: 'theme only', code: 'en', value: 'dark' },
    { name: 'both', code: 'es', value: 'dark' },
  ] as const)(
    'saves only the changed preferences ($name), locks the page and ends pristine (FR-08, BR-11, AC-09)',
    async ({ code, value }) => {
      await create();
      if (code !== 'en') await selectLanguage(code);
      if (value !== 'system') {
        cards()[2].click();
        fixture.detectChanges();
      }
      const wantsLanguage = code !== 'en';
      const wantsTheme = value !== 'system';

      button(wantsLanguage ? 'Guardar preferencias' : 'Save preferences').click();
      fixture.detectChanges();
      await tick();

      const languageRequests = http.match(`${API}/account/language`);
      const themeRequests = http.match(`${API}/account/theme`);
      expect(languageRequests.length).toBe(wantsLanguage ? 1 : 0);
      expect(themeRequests.length).toBe(wantsTheme ? 1 : 0);
      if (wantsLanguage) expect(languageRequests[0].request.body).toEqual({ language: 'es' });
      if (wantsTheme) {
        expect(themeRequests[0].request.body).toEqual({
          theme: 'dark',
          source: 'preferences_page',
        });
      }
      expect(root().textContent).toContain(wantsLanguage ? 'Guardando…' : 'Saving…');
      expect(button(wantsLanguage ? 'Guardando…' : 'Saving…').disabled).toBe(true);
      expect(cards().every((c) => c.getAttribute('aria-disabled') === 'true')).toBe(true);
      // A repeated click while saving sends nothing.
      root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
      expect(http.match(`${API}/account/language`).length).toBe(0);
      expect(http.match(`${API}/account/theme`).length).toBe(0);

      [...languageRequests, ...themeRequests].forEach((request: TestRequest) =>
        request.flush({ preferredLanguage: code, preferredTheme: value }),
      );
      await tick();

      expect(toasts()).toContain(wantsLanguage ? 'Preferencias guardadas' : 'Preferences saved');
      expect(button(wantsLanguage ? 'Guardar preferencias' : 'Save preferences').disabled).toBe(
        true,
      );
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe(wantsTheme ? 'dark' : 'system');
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe(code);
      expect(theme.persisted()).toBe(value);
      http.verify();
    },
  );

  it.each([
    { failed: 'language', status: 500 },
    { failed: 'theme', status: 0 },
    { failed: 'both', status: 400 },
    { failed: 'language', status: 401 },
  ] as const)(
    'restores only the failed preference and shows its toast ($failed, $status) (BR-12, AC-10)',
    async ({ failed, status }) => {
      await create();
      await selectLanguage('es');
      cards()[2].click();
      fixture.detectChanges();
      button('Guardar preferencias').click();
      await tick();

      const fail = (request: TestRequest) =>
        status === 0
          ? request.error(new ProgressEvent('error'))
          : request.flush({}, { status, statusText: 'x' });
      const succeed = (request: TestRequest) => request.flush({});
      const languageRequest = http.expectOne(`${API}/account/language`);
      const themeRequest = http.expectOne(`${API}/account/theme`);
      (failed === 'language' || failed === 'both' ? fail : succeed)(languageRequest);
      (failed === 'theme' || failed === 'both' ? fail : succeed)(themeRequest);
      await vi.waitFor(() => expect(toasts()).toMatch(/We couldn't save|No pudimos guardar/));
      await tick();

      const languageFailed = failed !== 'theme';
      const themeFailed = failed !== 'language';
      expect(language.lang()).toBe(languageFailed ? 'en' : 'es');
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe(languageFailed ? 'en' : 'es');
      expect(isDark()).toBe(!themeFailed);
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe(themeFailed ? 'system' : 'dark');
      const expected = {
        language: "We couldn't save your language. Try again.",
        theme: 'No pudimos guardar tu apariencia. Inténtalo de nuevo.',
        both: "We couldn't save your preferences. Try again.",
      }[failed];
      expect(toasts()).toContain(expected);
      expect(toasts()).not.toContain('Preferences saved');
      expect(toasts()).not.toContain('Preferencias guardadas');
      if (status === 401) {
        await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/auth/sign-in'));
      }
      http.verify();
    },
  );

  it('asks before discarding, applies the saved values on Discard and adopts a menu change (FR-09, BR-13, AC-11, AC-12)', async () => {
    await create();
    await selectLanguage('es');
    cards()[2].click();
    fixture.detectChanges();

    // A theme saved from a menu becomes the saved and selected value; the language stays pending.
    const menu = TestBed.inject(ThemePreferenceService);
    menu.choose('light', 'internal', 'user_menu');
    http.expectOne(`${API}/account/theme`).flush({ preferredTheme: 'light' });
    await tick();
    expect(cards().map((c) => c.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    expect(theme.persisted()).toBe('light');
    expect(button('Guardar preferencias').disabled).toBe(false);

    const dialogButton = (label: string) =>
      Array.from(document.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')).find(
        (b) => b.textContent?.trim() === label,
      );
    const leaving = fixture.componentInstance.canLeave();
    let outcome: boolean | undefined;
    (leaving as Observable<boolean>).subscribe((value) => (outcome = value));
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(dialogButton('Seguir editando')).toBeDefined();
    });
    dialogButton('Seguir editando')!.click();
    await tick();
    expect(outcome).toBe(false);
    expect(language.lang()).toBe('es');

    (fixture.componentInstance.canLeave() as Observable<boolean>).subscribe(
      (value) => (outcome = value),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(dialogButton('Descartar cambios')).toBeDefined();
    });
    dialogButton('Descartar cambios')!.click();
    await vi.waitFor(() => expect(outcome).toBe(true));
    expect(language.lang()).toBe('en');
    expect(theme.preference()).toBe('light');
    expect(fixture.componentInstance.canLeave()).toBe(true);
    http.verify();
  });
});
