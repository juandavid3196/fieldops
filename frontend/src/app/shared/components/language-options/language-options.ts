import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  LanguagePreferenceService,
  LanguageSurface,
} from '../../../core/i18n/language-preference.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { LanguageCode } from '../../../core/i18n/languages.config';

/**
 * The language options (BR-18): a `menu` of `menuitemradio` buttons with `aria-checked`, a check
 * icon and selected styling on the active one. Up/Down/Home/End move, Escape or Left Arrow ask the
 * host to close the list. While a save is in flight the options are `aria-disabled`.
 */
@Component({
  selector: 'app-language-options',
  imports: [TranslocoPipe],
  templateUrl: './language-options.html',
  styleUrl: './language-options.scss',
})
export class LanguageOptions {
  private readonly language = inject(LanguageService);
  private readonly preference = inject(LanguagePreferenceService);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly surface = input.required<LanguageSurface>();
  /** Emits after a choice so the host can close its menu and return focus. */
  readonly chosen = output<LanguageCode>();
  /** Escape or Left Arrow: the host closes the list and focuses its trigger. */
  readonly closeRequested = output<void>();

  readonly options = this.language.options;
  readonly active = this.language.lang;
  readonly saving = this.preference.saving;
  readonly activeIndex = computed(() =>
    Math.max(
      0,
      this.options.findIndex((option) => option.code === this.active()),
    ),
  );

  /** Focuses the active option (or the first). */
  focusActive(): void {
    this.items()[this.activeIndex()]?.focus();
  }

  choose(code: LanguageCode): void {
    this.preference.choose(code, this.surface());
    this.chosen.emit(code);
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
