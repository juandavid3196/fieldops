import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { LG_QUERY, watchMedia } from '../../../../core/config/breakpoints';
import { SessionService } from '../../../../core/services/session.service';
import { MessageList } from '../../components/message-list/message-list';
import { FORBIDDEN_MESSAGE, MESSAGING_ROLES } from '../../utils/messages-format';

import { t } from '../../../../core/i18n/translate';
export const SELECT_CONVERSATION_TEXT = (): string =>
  t('messages.messagesHost.selectConversationText');

/**
 * Messages screen of both shells. Technician (`split` false): the list, or the conversation when one
 * is open. Staff (`split` true): list and conversation side by side from 1024 px, one pane below.
 * The route data `messagesBase` is the list path; roles outside Messaging get the forbidden state.
 */
@Component({
  selector: 'app-messages-host',
  imports: [MessageList, RouterOutlet],
  templateUrl: './messages-host.html',
  styleUrl: './messages-host.scss',
})
export class MessagesHost {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly basePath =
    typeof this.route.snapshot.data['messagesBase'] === 'string'
      ? this.route.snapshot.data['messagesBase']
      : '/messages';
  readonly split = this.route.snapshot.data['split'] === true;
  readonly allowed = MESSAGING_ROLES.includes(inject(SessionService).session()?.role.code ?? '');
  get forbiddenMessage(): string {
    return FORBIDDEN_MESSAGE();
  }
  get selectText(): string {
    return SELECT_CONVERSATION_TEXT();
  }
  get selectHint(): string {
    return t('messages.messagesHost.chooseAJobOr');
  }

  readonly wide = signal(true);
  readonly selectedId = signal<string | null>(this.currentId());

  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.selectedId.set(this.currentId()));
    if (this.split) {
      const query = watchMedia(LG_QUERY, (matches) => this.wide.set(matches));
      this.wide.set(query.matches);
      this.destroyRef.onDestroy(query.stop);
    }
  }

  /** Two panes (staff, from 1024 px). */
  twoPanes(): boolean {
    return this.split && this.wide();
  }

  private currentId(): string | null {
    return this.route.snapshot.firstChild?.paramMap.get('conversationId') ?? null;
  }
}
