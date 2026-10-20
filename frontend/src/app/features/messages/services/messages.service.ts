import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  ConversationDetail,
  ConversationMessage,
  ConversationPage,
  MessageFilter,
  MessagePage,
  ResolveRequest,
} from '../models/messages.model';

/** `/messages` endpoints, shared by technicians and staff. The caller is resolved from the session. */
@Injectable({ providedIn: 'root' })
export class MessagesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(filter: MessageFilter, search: string, cursor: string | null): Observable<ConversationPage> {
    let params = new HttpParams().set('filter', filter);
    if (search !== '') {
      params = params.set('search', search);
    }
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return this.http.get<ConversationPage>(buildApiUrl(this.config, 'messages/conversations'), {
      params,
    });
  }

  detail(conversationId: string): Observable<ConversationDetail> {
    return this.http.get<ConversationDetail>(this.conversationUrl(conversationId));
  }

  messages(
    conversationId: string,
    cursor: { readonly before?: string; readonly after?: string } = {},
  ): Observable<MessagePage> {
    let params = new HttpParams();
    if (cursor.before !== undefined) {
      params = params.set('before', cursor.before);
    }
    if (cursor.after !== undefined) {
      params = params.set('after', cursor.after);
    }
    return this.http.get<MessagePage>(`${this.conversationUrl(conversationId)}/messages`, {
      params,
    });
  }

  send(
    conversationId: string,
    request: { readonly clientMessageId: string; readonly body: string },
  ): Observable<ConversationMessage> {
    return this.http.post<ConversationMessage>(
      `${this.conversationUrl(conversationId)}/messages`,
      request,
    );
  }

  markRead(conversationId: string): Observable<void> {
    return this.http.post<void>(`${this.conversationUrl(conversationId)}/read`, null);
  }

  /** Resolves (creating when needed) the Job or Team conversation; returns its id. */
  resolve(request: ResolveRequest): Observable<string> {
    return this.http
      .post<{ conversationId: string }>(
        buildApiUrl(this.config, 'messages/conversations/resolve'),
        request,
      )
      .pipe(map((response) => response.conversationId));
  }

  private conversationUrl(conversationId: string): string {
    return buildApiUrl(this.config, `messages/conversations/${encodeURIComponent(conversationId)}`);
  }
}
