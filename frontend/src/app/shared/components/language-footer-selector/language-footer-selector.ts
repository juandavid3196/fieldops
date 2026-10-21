import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { LanguageService } from '../../../core/i18n/language.service';
import { languageOption } from '../../../core/i18n/languages.config';
import { LanguageOptions } from '../language-options/language-options';

/**
 * Compact footer selector of the anonymous pages (BR-18): a button with a language icon, the active
 * option name and the accessible name "Language: English" / "Idioma: Español". It opens the same two
 * options; the choice only updates the local value. Escape returns focus to the button.
 */
@Component({
  selector: 'app-language-footer-selector',
  imports: [TranslocoPipe, LanguageOptions],
  templateUrl: './language-footer-selector.html',
  styleUrl: './language-footer-selector.scss',
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class LanguageFooterSelector {
  private readonly language = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');
  private readonly options = viewChild(LanguageOptions);

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

  onButtonKeydown(event: KeyboardEvent): void {
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && !this.open()) {
      event.preventDefault();
      this.openList();
    } else if (event.key === 'Escape' && this.open()) {
      event.preventDefault();
      this.close(true);
    }
  }

  close(returnFocus: boolean): void {
    this.open.set(false);
    if (returnFocus) {
      this.button().nativeElement.focus();
    }
  }

  onDocumentClick(event: Event): void {
    if (this.open() && event.target instanceof Node && !this.host.contains(event.target)) {
      this.close(false);
    }
  }

  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (this.open() && next instanceof Node && !this.host.contains(next)) {
      this.close(false);
    }
  }

  private openList(): void {
    this.open.set(true);
    afterNextRender(() => this.options()?.focusActive(), { injector: this.injector });
  }
}
