import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  ThemePreferenceService,
  ThemeSource,
  ThemeSurface,
} from '../../../core/theme/theme-preference.service';
import { THEME_PREFERENCES, ThemePreference } from '../../../core/theme/theme.config';
import { ThemeService } from '../../../core/theme/theme.service';

const LABEL_KEYS: Readonly<Record<ThemePreference, string>> = {
  system: 'selector.themeSystem',
  light: 'selector.themeLight',
  dark: 'selector.themeDark',
};

/**
 * The appearance options (BR-14): a `menu` of `menuitemradio` buttons with `aria-checked`, a check
 * icon and selected styling on the active one; same keyboard rules as the language options and
 * `aria-disabled` while a save is in flight. Shares the language options stylesheet.
 */
@Component({
  selector: 'app-appearance-options',
  imports: [TranslocoPipe],
  templateUrl: './appearance-options.html',
  styleUrl: '../language-options/language-options.scss',
})
export class AppearanceOptions {
  private readonly theme = inject(ThemeService);
  private readonly preference = inject(ThemePreferenceService);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  /** `anonymous`: pages without a session; the choice only updates the local value. */
  readonly surface = input.required<ThemeSurface | 'anonymous'>();
  readonly source = input<ThemeSource>();
  /** Emits after a choice so the host can close its menu and return focus. */
  readonly chosen = output<ThemePreference>();
  /** Escape or Left Arrow: the host closes the list and focuses its trigger. */
  readonly closeRequested = output<void>();

  readonly options = THEME_PREFERENCES.map((value) => ({ value, labelKey: LABEL_KEYS[value] }));
  readonly active = this.theme.preference;
  readonly saving = this.preference.saving;
  readonly activeIndex = computed(() =>
    Math.max(
      0,
      this.options.findIndex((option) => option.value === this.active()),
    ),
  );

  /** Focuses the active option (or the first). */
  focusActive(): void {
    this.items()[this.activeIndex()]?.focus();
  }

  choose(value: ThemePreference): void {
    const surface = this.surface();
    const source = this.source();
    if (surface === 'anonymous' || source === undefined) {
      this.theme.applyPreference(value);
    } else {
      this.preference.choose(value, surface, source);
    }
    this.chosen.emit(value);
  }

  onKeydown(event: KeyboardEvent): void {
    const items = this.items();
    const current = items.indexOf(document.activeElement as HTMLElement);
    let next: number;
    switch (event.key) {
      case 'ArrowDown':
        next = (current + 1) % items.length;
        break;
      case 'ArrowUp':
        next = current <= 0 ? items.length - 1 : current - 1;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = items.length - 1;
        break;
      case 'Escape':
      case 'ArrowLeft':
        event.preventDefault();
        event.stopPropagation();
        this.closeRequested.emit();
        return;
      default:
        return;
    }
    event.preventDefault();
    event.stopPropagation();
    items[next]?.focus();
  }

  private items(): HTMLElement[] {
    return Array.from(this.host.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
  }
}
