import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { LanguageSurface } from '../../../core/i18n/language-preference.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { languageOption } from '../../../core/i18n/languages.config';
import { LanguageOptions } from '../language-options/language-options';

/**
 * The **Language** item of the three shell menus (BR-18): shows the active option name as secondary
 * text and opens a sublist with English and Español. Enter, Space or Right Arrow open it; Escape or
 * Left Arrow close it with focus back on the item (a second Escape reaches the host menu). Choosing
 * emits `chosen` so the shell closes its menu and returns focus to the menu button.
 */
@Component({
  selector: 'app-language-menu-item',
  imports: [TranslocoPipe, LanguageOptions],
  templateUrl: './language-menu-item.html',
  styleUrl: './language-menu-item.scss',
})
export class LanguageMenuItem {
  private readonly language = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly options = viewChild(LanguageOptions);

  /** Where the choice is saved: the internal or the portal session. */
  readonly surface = input.required<Exclude<LanguageSurface, 'anonymous'>>();
  /** `null` when the host list is a plain panel of links (technician More), not an ARIA menu. */
  readonly menuRole = input<'menuitem' | null>('menuitem');
  readonly chosen = output<void>();

  readonly open = signal(false);

  activeName(): string {
    return languageOption(this.language.lang()).name;
  }

  activeCode(): string {
    return this.language.lang();
  }

  toggle(): void {
    if (this.open()) {
      this.close(true);
    } else {
      this.openList();
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' && !this.open()) {
      event.preventDefault();
      event.stopPropagation();
      this.openList();
    } else if (this.open() && (event.key === 'Escape' || event.key === 'ArrowLeft')) {
      event.preventDefault();
      event.stopPropagation();
      this.close(true);
    }
  }

  close(returnFocus: boolean): void {
    this.open.set(false);
    if (returnFocus) {
      this.trigger().nativeElement.focus();
    }
  }

  onChosen(): void {
    this.open.set(false);
    this.chosen.emit();
  }

  private openList(): void {
    this.open.set(true);
    afterNextRender(() => this.options()?.focusActive(), { injector: this.injector });
  }
}
