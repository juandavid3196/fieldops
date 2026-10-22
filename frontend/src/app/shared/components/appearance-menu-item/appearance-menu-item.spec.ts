import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../../../core/config/api.config';
import { ThemePreferenceService } from '../../../core/theme/theme-preference.service';
import { THEME_STORAGE_KEY } from '../../../core/theme/theme.config';
import { ThemeService } from '../../../core/theme/theme.service';
import { AppearanceMenuItem } from './appearance-menu-item';
import { AppearanceToast } from '../appearance-toast/appearance-toast';

const API = 'http://api.test';
const isDark = () => document.documentElement.classList.contains('app-dark');

describe('Appearance menu item in the three shells (FR-10, FR-11, BR-14, BR-15, AC-13, AC-14)', () => {
  @Component({
    selector: 'app-menu-host',
    imports: [AppearanceMenuItem, AppearanceToast],
    template: `<app-appearance-menu-item
        [surface]="surface"
        [source]="source"
        [menuRole]="menuRole"
        (chosen)="chosen.set(chosen() + 1)"
      />
      <app-appearance-toast />`,
  })
  class Host {
    surface: 'internal' | 'portal' = 'internal';
    source: 'user_menu' | 'technician_menu' | 'portal_menu' = 'user_menu';
    menuRole: 'menuitem' | null = 'menuitem';
    readonly chosen = signal(0);
  }

  it.each([
    {
      shell: 'main',
      surface: 'internal',
      source: 'user_menu',
      path: 'account/theme',
      menuRole: 'menuitem',
    },
    {
      shell: 'technician',
      surface: 'internal',
      source: 'technician_menu',
      path: 'account/theme',
      menuRole: null,
    },
    {
      shell: 'portal',
      surface: 'portal',
      source: 'portal_menu',
      path: 'portal/account/theme',
      menuRole: 'menuitem',
    },
  ] as const)(
    'opens by keyboard, saves with the $shell source and restores with the toast on failure',
    async ({ surface, source, path, menuRole }) => {
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: API_CONFIG, useValue: { baseUrl: API } },
        ],
      });
      const http = TestBed.inject(HttpTestingController);
      const theme = TestBed.inject(ThemeService);
      const preference = TestBed.inject(ThemePreferenceService);
      theme.init();
      const fixture = TestBed.createComponent(Host);
      Object.assign(fixture.componentInstance, { surface, source, menuRole });
      document.body.appendChild(fixture.nativeElement);
      fixture.detectChanges();
      const root: HTMLElement = fixture.nativeElement;
      const trigger = root.querySelector('button.lang-item') as HTMLButtonElement;
      const options = () =>
        Array.from(root.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
      const open = async () => {
        trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
      };

      expect(trigger.getAttribute('role')).toBe(menuRole);
      expect(trigger.textContent).toContain('Appearance');
      expect(trigger.textContent).toContain('System');

      // Keyboard: options with ARIA state and a check on the active one; Escape returns to the item.
      await open();
      expect(options().map((o) => o.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
      expect(options().map((o) => o.getAttribute('aria-checked'))).toEqual([
        'true',
        'false',
        'false',
      ]);
      expect(options()[0].querySelector('.pi-check')).not.toBeNull();
      options()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      fixture.detectChanges();
      expect(options().length).toBe(0);
      expect(document.activeElement).toBe(trigger);

      // A different option applies at once and saves with the shell source.
      await open();
      options()[2].click();
      const request = http.expectOne(`${API}/${path}`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ theme: 'dark', source });
      expect(isDark()).toBe(true);
      expect(preference.saving()).toBe(true);
      expect(fixture.componentInstance.chosen()).toBe(1);

      // Repeat choices while saving send nothing; options are aria-disabled.
      await open();
      expect(options().map((o) => o.getAttribute('aria-disabled'))).toEqual([
        'true',
        'true',
        'true',
      ]);
      options()[1].click();
      http.expectNone(`${API}/${path}`);
      request.flush({ preferredTheme: 'dark' });
      fixture.detectChanges();
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
      expect(preference.saving()).toBe(false);

      // Choosing the active option sends nothing.
      await open();
      options()[2].click();
      http.expectNone(`${API}/${path}`);

      // A failure restores the previous theme and local value and shows the toast.
      await open();
      options()[1].click();
      expect(isDark()).toBe(false);
      http.expectOne(`${API}/${path}`).flush({}, { status: 500, statusText: 'x' });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(isDark()).toBe(true);
      expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
      expect(document.body.textContent).toContain("We couldn't save your appearance. Try again.");
      http.verify();
      root.remove();
    },
  );
});
