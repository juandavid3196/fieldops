import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LanguageFooterSelector } from '../../../../shared/components/language-footer-selector/language-footer-selector';
/** Public layout route: header and footer shared by Privacy, Terms and the public Help page. */
@Component({
  selector: 'app-public-site-shell',
  imports: [LanguageFooterSelector, TranslocoPipe, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './public-site-shell.html',
  styleUrl: './public-site-shell.scss',
})
export class PublicSiteShell {
  protected readonly links = localizedList(
    () =>
      [
        { label: t('legal.publicSiteShell.privacy'), path: '/privacy' },
        { label: t('legal.publicSiteShell.terms'), path: '/terms' },
        { label: t('legal.publicSiteShell.help'), path: '/support' },
      ] as const,
  );
  protected readonly year = new Date().getFullYear();
}
