import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Drawer } from 'primeng/drawer';
import { FocusTrap } from 'primeng/focustrap';

import { DOCK_QUERY, MD_QUERY, watchMedia } from '../../../core/config/breakpoints';

export type DrawerMode = 'docked' | 'overlay' | 'fullscreen';

/**
 * Record drawer chrome shared by the branch and user drawers: a labelled non-modal docked
 * region from 1440px, a modal overlay below the top bar from 768px and a full-screen dialog
 * below. Consumers project `[drawerHeader]`, `[drawerBody]` and `[drawerFooter]` (use
 * `ng-container ngProjectAs`) and own the dirty guard behind `closeRequested`.
 */
@Component({
  selector: 'app-drawer-shell',
  imports: [NgTemplateOutlet, Drawer, FocusTrap],
  templateUrl: './drawer-shell.html',
  styleUrl: './drawer-shell.scss',
})
export class DrawerShell {
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly hostElement: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly open = input.required<boolean>();
  /** Id of the projected heading; it labels the dialog/region and receives focus on open. */
  readonly titleId = input.required<string>();
  /** Panel inline size (any CSS length). */
  readonly size = input('var(--fo-size-drawer)');
  /** `false`: never docked beside the page; overlay from md, full screen below. */
  readonly dockable = input(true);

  readonly closeRequested = output<void>();

  readonly isDocked = signal(false);
  readonly isBelowMd = signal(false);
  readonly mode = computed<DrawerMode>(() =>
    this.dockable() && this.isDocked() ? 'docked' : this.isBelowMd() ? 'fullscreen' : 'overlay',
  );

  /** Token variables (`--p-drawer-*`) plus, from md, the panel below the top bar. */
  readonly overlayStyle = computed(() => ({
    '--p-drawer-shadow': 'var(--fo-shadow-drawer)',
    '--p-drawer-header-padding': '0',
    '--p-drawer-footer-padding': '0',
    // Same body inset as the docked panel (Aura's default has no top padding).
    '--p-drawer-content-padding': '1.25rem',
    ...(this.isBelowMd()
      ? {}
      : {
          'inline-size': this.size(),
          'inset-block-start': 'var(--fo-size-topbar)',
          'block-size': 'calc(100dvh - var(--fo-size-topbar))',
        }),
  }));
  readonly maskStyle = {
    'inset-block-start': 'var(--fo-size-topbar)',
    background: 'var(--fo-color-mask)',
  };
  /** `Drawer` has no role input: the dialog semantics land on its rendered root via `pt.root`. */
  readonly drawerPt = computed(() => ({
    root: { role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': this.titleId() },
  }));

  /** The element focused before the drawer opened; restored to it on close. */
  private previouslyFocusedElement: HTMLElement | null = null;

  constructor() {
    let wasOpen = false;
    effect(() => {
      const isOpen = this.open();
      if (isOpen) {
        if (!wasOpen) {
          this.previouslyFocusedElement =
            typeof document !== 'undefined' ? (document.activeElement as HTMLElement | null) : null;
          // The docked region has no open transition to wait for.
          afterNextRender(() => this.onDockedShow(), { injector: this.injector });
        }
      } else if (wasOpen && this.mode() === 'docked') {
        this.onDrawerHide();
      }
      wasOpen = isOpen;
    });

    const dock = watchMedia(DOCK_QUERY, (matches) => this.isDocked.set(matches), false);
    const md = watchMedia(MD_QUERY, (matches) => this.isBelowMd.set(!matches));
    this.isDocked.set(dock.matches);
    this.isBelowMd.set(!md.matches);
    this.destroyRef.onDestroy(() => {
      dock.stop();
      md.stop();
    });
  }

  private onDockedShow(): void {
    if (this.mode() === 'docked') {
      this.onDrawerShow();
    }
  }

  /** Esc closes the docked region (the overlay drawer handles Esc itself). */
  onDockedKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeRequested.emit();
    }
  }

  /** Moves focus into the labelled dialog once PrimeNG's open transition settles. */
  onDrawerShow(): void {
    this.hostElement.querySelector<HTMLElement>(`#${this.titleId()}`)?.focus();
  }

  /** Returns focus to the row or button that opened the drawer. */
  onDrawerHide(): void {
    this.previouslyFocusedElement?.focus();
    this.previouslyFocusedElement = null;
  }
}
