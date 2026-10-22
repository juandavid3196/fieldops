import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
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
import { SubmenuCoordinator } from './submenu-coordinator';

/** 14rem panel + 0.75rem gap + 0.5rem margin from the screen edge, at the 16px root size. */
const FLYOUT_ROOM_PX = 14 * 16 + 12 + 8;

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
  private readonly submenus = inject(SubmenuCoordinator);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly options = viewChild(LanguageOptions);

  /** Where the choice is saved: the internal or the portal session. */
  readonly surface = input.required<Exclude<LanguageSurface, 'anonymous'>>();
  /** `null` when the host list is a plain panel of links (technician More), not an ARIA menu. */
  readonly menuRole = input<'menuitem' | null>('menuitem');
  readonly chosen = output<void>();

  readonly open = signal(false);
  /** The sublist floats beside the menu when there is room for it; otherwise it opens inline. */
  readonly flyout = signal(false);

  constructor() {
    effect(() => {
      if (this.submenus.active() !== this && this.open()) {
        this.open.set(false);
      }
    });
  }

  activeName(): string {
    return languageOption(this.language.lang()).name;
  }

  activeCode(): string {
    return this.language.lang();
  }

  /** A pointer click (`detail > 0`) opens the list without moving focus, so no focus ring shows. */
  toggle(event?: MouseEvent): void {
    if (this.open()) {
      this.close(true);
    } else {
      this.openList(event === undefined || event.detail === 0);
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

  private openList(focusOptions = true): void {
    this.flyout.set(this.hasRoomForFlyout());
    this.submenus.active.set(this);
    this.open.set(true);
    if (focusOptions) {
      afterNextRender(() => this.options()?.focusActive(), { injector: this.injector });
    }
  }

  /** Room on the leading side of the menu item for the flyout (width + gap + screen margin). */
  private hasRoomForFlyout(): boolean {
    const rect = this.host.getBoundingClientRect();
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    const room = rtl ? window.innerWidth - rect.right : rect.left;
    return room >= FLYOUT_ROOM_PX;
  }
}
