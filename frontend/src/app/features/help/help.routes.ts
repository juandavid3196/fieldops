import { ActivatedRouteSnapshot, Routes } from '@angular/router';

import { HelpAudience } from './models/help.model';
import { findArticle, findTopic } from './utils/help-content';

/**
 * The help center routes of one audience (FR-16, BR-13). The same pages serve `/help` and
 * `/portal/help`; the leaf route data tells them which topics and base path to use.
 */
export function createHelpRoutes(audience: HelpAudience): Routes {
  const baseTitle =
    audience === 'portal' ? 'Help & support · Customer portal' : 'Help & support · FieldOps';
  const data = { helpAudience: audience };

  return [
    {
      path: '',
      pathMatch: 'full',
      title: baseTitle,
      data,
      loadComponent: () => import('./pages/help-home/help-home').then((m) => m.HelpHome),
    },
    {
      path: ':topicSlug',
      pathMatch: 'full',
      title: (route: ActivatedRouteSnapshot) =>
        `${findTopic(route.paramMap.get('topicSlug'), audience)?.title ?? 'Article not found'} · ${baseTitle}`,
      data,
      loadComponent: () => import('./pages/help-topic/help-topic').then((m) => m.HelpTopicPage),
    },
    {
      path: ':topicSlug/:articleSlug',
      title: (route: ActivatedRouteSnapshot) =>
        `${
          findArticle(route.paramMap.get('topicSlug'), route.paramMap.get('articleSlug'), audience)
            ?.article.title ?? 'Article not found'
        } · ${baseTitle}`,
      data,
      loadComponent: () =>
        import('./pages/help-article/help-article').then((m) => m.HelpArticlePage),
    },
  ];
}

/** Authenticated help center; mounted inside the app shell, which owns the auth guard (BR-01). */
export default createHelpRoutes('internal');
