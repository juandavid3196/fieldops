import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PrimeNG } from 'primeng/config';
import { firstValueFrom, throwError } from 'rxjs';

import { LanguageFooterSelector } from '../../shared/components/language-footer-selector/language-footer-selector';
import { LanguageMenuItem } from '../../shared/components/language-menu-item/language-menu-item';
import { LanguageOptions } from '../../shared/components/language-options/language-options';
import { LanguageToast } from '../../shared/components/language-toast/language-toast';
import { notificationText } from '../../features/notifications/utils/notification-text';
import { API_CONFIG } from '../config/api.config';
import { ApiErrorService } from '../services/api-error.service';
import { PortalSessionService } from '../services/portal-session.service';
import { SessionService } from '../services/session.service';
import { activeIntlLocale } from './active-locale';
import { FieldErrorCatalog } from './field-error-catalog';
import { FieldOpsMissingHandler } from './i18n-missing.handler';
import { I18n } from './i18n.service';
import { withI18n } from './i18n-scopes';
import { LanguagePreferenceService } from './language-preference.service';
import { LanguageService } from './language.service';
import { LANGUAGE_STORAGE_KEY } from './languages.config';

const API = 'http://api.test';

function provideApi() {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { baseUrl: API } },
  ];
}

function stubBrowser(languages: readonly string[], language: string | undefined = languages[0]) {
  vi.spyOn(window.navigator, 'languages', 'get').mockReturnValue(languages);
  vi.spyOn(window.navigator, 'language', 'get').mockReturnValue(language ?? '');
}

async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

afterEach(() => vi.restoreAllMocks());

describe('language resolution and persistence (FR-01, FR-02, AC-01, AC-02)', () => {
  it.each([
    { local: 'es', browser: ['en-US'], expected: 'es' },
    { local: 'en', browser: ['es-CO'], expected: 'en' },
    { local: null, browser: ['es'], expected: 'es' },
    { local: null, browser: ['es-419'], expected: 'es' },
    { local: null, browser: ['ES-mx'], expected: 'es' },
    { local: null, browser: ['pt-BR'], expected: 'en' },
    { local: null, browser: ['fr'], expected: 'en' },
    { local: null, browser: [] as string[], expected: 'en' },
    { local: 'xx', browser: ['es'], expected: 'es' },
  ])('resolves local $local with browser $browser to $expected', async (testCase) => {
    stubBrowser(testCase.browser, testCase.browser[0]);
    if (testCase.local !== null) {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, testCase.local);
    }
    const language = TestBed.inject(LanguageService);

    await firstValueFrom(language.init());

    expect(language.lang()).toBe(testCase.expected);
    expect(document.documentElement.lang).toBe(testCase.expected);
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe(testCase.expected);
  });

  it('applies a supported session preference without a request and ignores an unsupported one', async () => {
    TestBed.configureTestingModule({ providers: provideApi() });
    const http = TestBed.inject(HttpTestingController);
    const internal = TestBed.inject(SessionService);
    const portal = TestBed.inject(PortalSessionService);
    const language = TestBed.inject(LanguageService);
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');
    const user = { id: 'u', firstName: 'A', lastName: 'B', email: 'a@b.c', photoUpdatedAt: null };
    const internalSession = (preferredLanguage: string) => ({
      user: { ...user, preferredLanguage },
      organization: { id: 'o', name: 'Org' },
      role: { code: 'owner', name: 'Owner' },
    });

    const internalLoad = firstValueFrom(internal.loadCurrent());
    http.expectOne(`${API}/sessions/current`).flush(internalSession('es'));
    await internalLoad;
    expect(language.lang()).toBe('es');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('es');

    await firstValueFrom(language.apply('en'));
    const portalLoad = firstValueFrom(portal.loadCurrent());
    http.expectOne(`${API}/portal/sessions/current`).flush({
      user: { firstName: 'A', lastName: 'B', email: 'a@b.c', preferredLanguage: 'es' },
      account: { contactId: 'c', customerName: 'C', organizationName: 'O', hasLogo: false },
      accounts: [],
    });
    await portalLoad;
    expect(language.lang()).toBe('es');

    const unsupported = firstValueFrom(internal.loadCurrent());
    http.expectOne(`${API}/sessions/current`).flush(internalSession('pt'));
    await unsupported;
    expect(language.lang()).toBe('es');
    http.verify();
  });
});

describe('authenticated switch (FR-05, BR-09, AC-10)', () => {
  @Component({
    selector: 'app-language-host',
    imports: [LanguageOptions, LanguageToast],
    template: '<app-language-options surface="internal" /><app-language-toast />',
  })
  class Host {}

  it.each([
    {
      name: '500',
      fail: (request: TestRequest) => request.flush({}, { status: 500, statusText: 'x' }),
    },
    {
      name: '400',
      fail: (request: TestRequest) => request.flush({}, { status: 400, statusText: 'x' }),
    },
    {
      name: '401',
      fail: (request: TestRequest) => request.flush({}, { status: 401, statusText: 'x' }),
    },
    { name: 'network', fail: (request: TestRequest) => request.error(new ProgressEvent('error')) },
  ])('saves the choice, disables the options meanwhile and restores on $name', async ({ fail }) => {
    TestBed.configureTestingModule({ providers: provideApi() });
    const http = TestBed.inject(HttpTestingController);
    const language = TestBed.inject(LanguageService);
    const preference = TestBed.inject(LanguagePreferenceService);
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const options = () => Array.from(root.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));

    options()[1].click();
    const request = await vi.waitFor(() => http.expectOne(`${API}/account/language`));
    fixture.detectChanges();

    expect(request.request.body).toEqual({ language: 'es' });
    expect(language.lang()).toBe('es');
    expect(preference.saving()).toBe(true);
    expect(options().map((option) => option.getAttribute('aria-disabled'))).toEqual([
      'true',
      'true',
    ]);

    // A repeated choice while saving sends nothing.
    options()[0].click();
    fail(request);
    await vi.waitFor(() => expect(language.lang()).toBe('en'));
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();

    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
    expect(options().map((option) => option.getAttribute('aria-disabled'))).toEqual([
      'false',
      'false',
    ]);
    expect(document.body.textContent).toContain("We couldn't save your language. Try again.");
    http.verify();
  });
});

describe('runtime switch (FR-04, AC-04)', () => {
  @Component({
    selector: 'app-switch-host',
    imports: [FormsModule, TranslocoPipe],
    template: `<input [(ngModel)]="value" />
      <p>{{ 'common.save' | transloco }}</p>`,
  })
  class Host {
    readonly value = signal('typed text');
  }

  it('changes texts, html lang and title without touching the form or the URL', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const language = TestBed.inject(LanguageService);
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    const input = root.querySelector('input') as HTMLInputElement;
    input.value = 'edited by the user';
    input.dispatchEvent(new Event('input'));
    const href = window.location.href;

    expect(root.querySelector('p')?.textContent).toBe('Save');
    await firstValueFrom(language.apply('es'));
    fixture.detectChanges();

    expect(root.querySelector('p')?.textContent).toBe('Guardar');
    expect(input.value).toBe('edited by the user');
    expect(document.documentElement.lang).toBe('es');
    expect(window.location.href).toBe(href);
  });
});

describe('selectors (FR-03, FR-18, BR-18, AC-03, AC-11)', () => {
  it('opens the Language sublist by keyboard with ARIA state and returns focus', async () => {
    TestBed.configureTestingModule({ providers: provideApi() });
    @Component({
      selector: 'app-menu-host',
      imports: [LanguageMenuItem],
      template: `<app-language-menu-item surface="internal" (chosen)="chosen.set(chosen() + 1)" />`,
    })
    class Host {
      readonly chosen = signal(0);
    }
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const trigger = root.querySelector('button.lang-item') as HTMLButtonElement;
    document.body.appendChild(root);

    expect(trigger.textContent).toContain('Language');
    expect(trigger.textContent).toContain('English');
    trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const options = Array.from(root.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
    expect(options.map((option) => option.textContent?.trim())).toEqual(['English', 'Español']);
    expect(options.map((option) => option.getAttribute('aria-checked'))).toEqual(['true', 'false']);
    expect(options[0].querySelector('.pi-check')).not.toBeNull();
    expect(options[1].getAttribute('lang')).toBe('es');

    options[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(options[1]);
    options[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(root.querySelector('[role="menuitemradio"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    root.remove();
  });

  it('names the footer button by the active language and returns focus on Escape', async () => {
    TestBed.configureTestingModule({ providers: provideApi() });
    const language = TestBed.inject(LanguageService);
    const fixture = TestBed.createComponent(LanguageFooterSelector);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toBe('Language: English');

    button.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    const spanish = Array.from(root.querySelectorAll<HTMLElement>('[role="menuitemradio"]'))[1];
    spanish.click();
    await vi.waitFor(() => expect(language.lang()).toBe('es'));
    fixture.detectChanges();

    expect(button.getAttribute('aria-label')).toBe('Idioma: Español');
    expect(document.activeElement).toBe(button);
    fixture.nativeElement.remove();
  });
});

describe('formatting (FR-13, BR-10, AC-16)', () => {
  it('follows en-US and es-CO, including the PrimeNG texts', async () => {
    const language = TestBed.inject(LanguageService);
    const primeng = TestBed.inject(PrimeNG);
    await firstValueFrom(language.apply('en'));
    expect(activeIntlLocale()).toBe('en-US');
    expect((1234.5).toLocaleString(activeIntlLocale())).toBe('1,234.5');
    expect(primeng.getTranslation('monthNames')[0]).toBe('January');

    await firstValueFrom(language.apply('es'));
    expect(activeIntlLocale()).toBe('es-CO');
    expect((1234.5).toLocaleString(activeIntlLocale())).toBe('1.234,5');
    expect(primeng.getTranslation('monthNames')[0]).toBe('Enero');
    expect(primeng.getTranslation('dayNames')[0]).toBe('Domingo');
    expect(primeng.getTranslation('firstDayOfWeek')).toBe(0);
    expect(primeng.getTranslation('today')).toBe('Hoy');
  });
});

describe('error catalog and code labels (FR-11, FR-12, BR-13, BR-14, AC-17, AC-18)', () => {
  it('localizes kinds, known codes, field messages, roles, audit actions and notifications', async () => {
    const language = TestBed.inject(LanguageService);
    const errors = TestBed.inject(ApiErrorService);
    const catalog = TestBed.inject(FieldErrorCatalog);
    const i18n = TestBed.inject(I18n);
    await firstValueFrom(language.apply('es'));

    const known = errors.toApiError(
      new HttpErrorResponse({
        status: 409,
        error: { code: 'another_visit_active', title: 'English backend' },
      }),
    );
    const unknown = errors.toApiError(
      new HttpErrorResponse({ status: 409, error: { code: 'nope', title: 'English backend' } }),
    );
    const server = errors.toApiError(
      new HttpErrorResponse({ status: 500, error: { traceId: 'trace-1', detail: 'secret stack' } }),
    );

    expect(known.message).toBe('Ya hay otra visita activa. Termínala o ponla en pausa primero.');
    expect(unknown.message).toBe('El recurso cambió o ya existe. Actualiza e inténtalo de nuevo.');
    expect(server.message).toBe('Ocurrió un error inesperado. Inténtalo de nuevo más tarde.');
    expect(server.traceId).toBe('trace-1');
    expect(JSON.stringify(server)).not.toContain('secret');
    expect(catalog.translate('language', 'Choose a supported language.')).toBe(
      'Elige un idioma compatible.',
    );
    expect(catalog.translate('email', 'Whatever the backend says')).toBe('Revisa este valor.');
    expect(i18n.label('roles', 'owner', 'Owner')).toBe('Propietario');
    expect(i18n.label('roles', 'brand_new_role', 'Brand new')).toBe('Brand new');
    expect(i18n.label('auditActions', 'auth.signed_in', 'Signed in')).toBe('Inicio de sesión');
    expect(notificationText('Quote Q-7 was approved', 'quote.approved', 'title')).toBe(
      'La cotización Q-7 fue aprobada',
    );
    expect(notificationText('Some stored subject', 'quote.approved', 'title')).toBe(
      'Some stored subject',
    );
  });
});

describe('missing keys (FR-14, BR-11, AC-15)', () => {
  it('renders the key and warns once in development, and the English value in production', () => {
    const handler = TestBed.runInInjectionContext(() => new FieldOpsMissingHandler());
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const data = { activeLang: 'xx' } as never;

    expect(handler.handle('nowhere.key', data)).toBe('nowhere.key');
    expect(handler.handle('nowhere.key', data)).toBe('nowhere.key');
    expect(warn).toHaveBeenCalledTimes(1);

    vi.stubGlobal('ngDevMode', false);
    try {
      expect(handler.handle('common.save', data)).toBe('Save');
      expect(warn).toHaveBeenCalledTimes(1);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

describe('lazy feature scopes (FR-17, BR-20, AC-22)', () => {
  @Component({ selector: 'app-scope-stub', template: '' })
  class Stub {}

  it('loads a scope on activation, reloads it on a switch and survives a failed load', async () => {
    TestBed.resetTestingModule();
    const { provideI18n } = await import('./i18n.providers');
    TestBed.configureTestingModule({
      providers: [
        provideI18n(),
        provideRouter([withI18n({ path: 'customers', component: Stub }, 'customers')]),
      ],
    });
    const transloco = TestBed.inject(TranslocoService);
    const language = TestBed.inject(LanguageService);
    const router = TestBed.inject(Router);
    await TestBed.inject(LanguageService).init().toPromise();
    const key = 'customers.customerDetail.overview';

    expect(transloco.getTranslation('en')[key]).toBeUndefined();
    await router.navigateByUrl('/customers');
    expect(transloco.translate(key)).toBe('Overview');

    await firstValueFrom(language.apply('es'));
    expect(transloco.translate(key)).toBe('Resumen');

    const load = transloco.load.bind(transloco);
    vi.spyOn(transloco, 'load').mockImplementation((path, options) =>
      path.startsWith('customers/') ? throwError(() => new Error('offline')) : load(path, options),
    );
    await expect(firstValueFrom(language.apply('en'))).resolves.toBe('en');
    expect(transloco.translate(key)).toBe('Overview');
  });
});
