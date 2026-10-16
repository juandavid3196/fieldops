import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

interface AccountSection {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
}

/** FR-02: exactly these two sections; there is no Preferences item. */
const SECTIONS: readonly AccountSection[] = [
  { label: 'My profile', icon: 'pi-user', link: '/account/profile' },
  { label: 'Security', icon: 'pi-shield', link: '/account/security' },
];

/** Account settings frame: breadcrumb, heading, section navigation and the content card. */
@Component({
  selector: 'app-account-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './account-layout.html',
  styleUrl: './account-layout.scss',
})
export class AccountLayout {
  private readonly router = inject(Router);

  readonly sections = SECTIONS;
  readonly currentLabel = signal(this.labelFor());

  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.currentLabel.set(this.labelFor()));
  }

  private labelFor(): string {
    const path = this.router.url.split(/[?#]/)[0];
    return SECTIONS.find((section) => section.link === path)?.label ?? SECTIONS[0].label;
  }
}
