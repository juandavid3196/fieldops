import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { ThemeSource, ThemeSurface } from '../../../core/theme/theme-preference.service';
import { ThemeService } from '../../../core/theme/theme.service';
import { AppearanceOptions } from '../appearance-options/appearance-options';
import { SubmenuCoordinator } from '../language-menu-item/submenu-coordinator';

/** 14rem panel + 0.75rem gap + 0.5rem margin from the screen edge, at the 16px root size. */
const FLYOUT_ROOM_PX = 14 * 16 + 12 + 8;

const LABEL_KEYS = {
  system: 'selector.themeSystem',
  light: 'selector.themeLight',
  dark: 'selector.themeDark',
} as const;

/**
 * The **Appearance** item of the three shell menus (BR-14), placed after **Language**: shows the
 * active option label as secondary text and opens a sublist with System, Light and Dark. Keyboard
 * and focus rules are those of the Language item. Choosing emits `chosen` so the shell closes its
 * menu and returns focus to the menu button.
 */
@Component({
  selector: 'app-appearance-menu-item',
  imports: [TranslocoPipe, AppearanceOptions],
  templateUrl: './appearance-menu-item.html',
  styleUrl: '../language-menu-item/language-menu-item.scss',
})
export class AppearanceMenuItem {
  private readonly theme = inject(ThemeService);
  private readonly submenus = inject(SubmenuCoordinator);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly options = viewChild(AppearanceOptions);

  /** Whose session saves the choice. */
  readonly surface = input.required<ThemeSurface>();
  /** The audit source accepted by the endpoint of the surface (BR-06). */
  readonly source = input.required<ThemeSource>();
  /** `null` when the host list is a plain panel of links (technician More), not an ARIA menu. */
  readonly menuRole = input<'menuitem' | null>('menuitem');
  readonly chosen = output<void>();

  readonly open = signal(false);
  /** The sublist floats beside the menu when there is room for it; otherwise it opens inline. */
  readonly flyout = signal(false);
  readonly activeKey = computed(() => LABEL_KEYS[this.theme.preference()]);

  constructor() {
    effect(() => {
      if (this.submenus.active() !== this && this.open()) {
        this.open.set(false);
      }
    });
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

  private hasRoomForFlyout(): boolean {
    const rect = this.host.getBoundingClientRect();
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    const room = rtl ? window.innerWidth - rect.right : rect.left;
    return room >= FLYOUT_ROOM_PX;
  }
}
