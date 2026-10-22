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

import { ThemeService } from '../../../core/theme/theme.service';
import { AppearanceOptions } from '../appearance-options/appearance-options';

const LABEL_KEYS = {
  system: 'selector.themeSystem',
  light: 'selector.themeLight',
  dark: 'selector.themeDark',
} as const;

/**
 * Compact footer appearance selector of the anonymous pages, beside the language selector: a button
 * with a palette icon, the active option label and the accessible name "Appearance: System". It
 * opens System, Light and Dark; the choice only updates the local value (no session, no request).
 * Escape returns focus to the button.
 */
@Component({
  selector: 'app-appearance-footer-selector',
  imports: [TranslocoPipe, AppearanceOptions],
  templateUrl: './appearance-footer-selector.html',
  styleUrl: '../language-footer-selector/language-footer-selector.scss',
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class AppearanceFooterSelector {
  private readonly theme = inject(ThemeService);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');
  private readonly options = viewChild(AppearanceOptions);

  readonly open = signal(false);

  activeKey(): string {
    return LABEL_KEYS[this.theme.preference()];
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
