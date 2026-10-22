import { ActivatedRouteSnapshot, Routes } from '@angular/router';

import { t } from '../../core/i18n/translate';
import { HelpAudience } from './models/help.model';
import { findArticle, findTopic } from './utils/help-content';

/**
 * The help center routes of one audience (FR-16, BR-13). The same pages serve `/help` and
 * `/portal/help`; the leaf route data tells them which topics and base path to use. Copies
 * mounted elsewhere (public `/support`, technician shell) pass their own `base`.
 */
export function createHelpRoutes(audience: HelpAudience, base?: string): Routes {
  const baseKey = audience === 'portal' ? 'titles.portalHelpAndSupport' : 'titles.helpAndSupport';
  const baseTitle = (): string => t(baseKey);
  const data =
    base === undefined ? { helpAudience: audience } : { helpAudience: audience, helpBase: base };

  return [
    {
      path: '',
      pathMatch: 'full',
      title: baseKey,
      data,
      loadComponent: () => import('./pages/help-home/help-home').then((m) => m.HelpHome),
    },
    {
      path: ':topicSlug',
      pathMatch: 'full',
      title: (route: ActivatedRouteSnapshot) =>
        `${findTopic(route.paramMap.get('topicSlug'), audience)?.title ?? t('titles.articleNotFound')} · ${baseTitle()}`,
      data,
      loadComponent: () => import('./pages/help-topic/help-topic').then((m) => m.HelpTopicPage),
    },
    {
      path: ':topicSlug/:articleSlug',
      title: (route: ActivatedRouteSnapshot) =>
        `${
          findArticle(route.paramMap.get('topicSlug'), route.paramMap.get('articleSlug'), audience)
            ?.article.title ?? t('titles.articleNotFound')
        } · ${baseTitle()}`,
      data,
      loadComponent: () =>
        import('./pages/help-article/help-article').then((m) => m.HelpArticlePage),
    },
  ];
}

/** Base path of the public help pages linked from sign-in (no session). */
export const PUBLIC_HELP_BASE = '/support';

/**
 * Public copy of the internal help center: its own home page, and the internal topic and article
 * pages with links kept under `/support`.
 */
export function createPublicHelpRoutes(): Routes {
  const data = { helpAudience: 'internal', helpBase: PUBLIC_HELP_BASE };
  return [
    {
      path: '',
      pathMatch: 'full',
      title: 'titles.helpAndSupport',
      data,
      loadComponent: () => import('./pages/public-help/public-help').then((m) => m.PublicHelp),
    },
    ...createHelpRoutes('internal', PUBLIC_HELP_BASE).filter((route) => route.path !== ''),
  ];
}

/** Authenticated help center; mounted inside the app shell, which owns the auth guard (BR-01). */
export default createHelpRoutes('internal');
