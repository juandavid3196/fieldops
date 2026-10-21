import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { filter } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { t } from '../../../../core/i18n/translate';
interface AccountSection {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
}

/** FR-02: exactly these two sections; there is no Preferences item. */
function sectionsFor(base: string): readonly AccountSection[] {
  return [
    { label: t('account.accountLayout.myProfile'), icon: 'pi-user', link: `${base}/profile` },
    { label: t('account.accountLayout.security'), icon: 'pi-shield', link: `${base}/security` },
  ];
}

/** Account settings frame: breadcrumb, heading, section navigation and the content card. */
@Component({
  selector: 'app-account-layout',
  imports: [TranslocoPipe, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './account-layout.html',
  styleUrl: './account-layout.scss',
})
export class AccountLayout {
  private readonly router = inject(Router);
  private readonly accountBase: unknown = inject(ActivatedRoute).snapshot.data['accountBase'];

  /** `/account` by default; the technician shell mounts its own copy (`data.accountBase`). */
  readonly sections = sectionsFor(
    typeof this.accountBase === 'string' ? this.accountBase : '/account',
  );
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
    return this.sections.find((section) => section.link === path)?.label ?? this.sections[0].label;
  }
}
