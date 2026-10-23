import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';

export const MESSAGES_TITLE = 'shell.routeData.messages';
export const TECHNICIAN_MESSAGES_PATH = '/today/messages';
export const STAFF_MESSAGES_PATH = '/messages';

function messagesRoute(path: string, base: string, split: boolean): Route {
  const data = { shellTitle: MESSAGES_TITLE, messagesBase: base, split };
  // Loads the messages scope (and its dependencies) so a direct load or refresh is translated.
  return withI18n(
    {
      path,
      title: 'titles.messages',
      data,
      loadComponent: () =>
        import('./pages/messages-host/messages-host').then((m) => m.MessagesHost),
      children: [
        { path: '', pathMatch: 'full', data, children: [] },
        {
          path: ':conversationId',
          data,
          loadComponent: () =>
            import('./pages/conversation/conversation').then((m) => m.Conversation),
        },
      ],
    },
    'messages',
  );
}

/** Technician shell: `/today/messages` and `/today/messages/:conversationId` (single pane). */
export const technicianMessagesRoute: Route = messagesRoute(
  'today/messages',
  TECHNICIAN_MESSAGES_PATH,
  false,
);

/** The Messages Coming soon destination was replaced by the real page (BR-15). */
export const technicianMessagesSoonRedirect: Route = {
  path: 'today/soon/messages',
  pathMatch: 'full',
  redirectTo: TECHNICIAN_MESSAGES_PATH,
};

/** Staff shell: `/messages` and `/messages/:conversationId` (two panes from 1024 px). */
export const staffMessagesRoute: Route = messagesRoute('messages', STAFF_MESSAGES_PATH, true);
