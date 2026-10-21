import { isApiError } from '../../../core/models/api-error.model';
import { ConversationSummary } from '../models/messages.model';

import { t } from '../../../core/i18n/translate';
export const MAX_BODY_LENGTH = 2000;
export const COUNTER_FROM = 1800;
export const SEARCH_MIN_LENGTH = 2;

export const LIST_ERROR_MESSAGE = (): string => t('messages.messagesFormat.listErrorMessage');
export const CONVERSATION_ERROR_MESSAGE = (): string =>
  t('messages.messagesFormat.conversationErrorMessage');
export const NOT_AVAILABLE_MESSAGE = (): string => t('messages.messagesFormat.notAvailableMessage');
export const OPEN_ERROR_MESSAGE = (): string => t('messages.messagesFormat.openErrorMessage');
export const FORBIDDEN_MESSAGE = (): string => t('messages.messagesFormat.forbiddenMessage');
export const BODY_ERROR_MESSAGE = (): string => t('messages.messagesFormat.bodyErrorMessage');
export const RATE_LIMIT_MESSAGE = (): string => t('messages.messagesFormat.rateLimitMessage');

/** Roles with the Messaging policy (UX only; the backend decides). */
export const MESSAGING_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'technician',
];

export function unreadText(count: number): string {
  return t('messages.messagesFormat.unread', {
    count: count,
    value:
      count === 1 ? t('messages.messagesFormat.message') : t('messages.messagesFormat.messages'),
  });
}

/** "<first name>: <text>", "You: <text>" or "No messages yet" (BR-16). */
export function previewLine(conversation: ConversationSummary): string {
  const last = conversation.lastMessage;
  if (last === null) {
    return t('messages.messagesFormat.noMessagesYet');
  }
  const who = last.isMine
    ? t('messages.messagesFormat.you')
    : (last.authorName.trim().split(/\s+/)[0] ?? '');
  return `${who}: ${last.preview}`;
}

function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

const DAY_MS = 86_400_000;

function shortDate(date: Date): string {
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

/** "2 min", "1h", "Yesterday" or "MMM d" in the device zone (BR-16). */
export function relativeTime(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const minutes = Math.floor((now.getTime() - date.getTime()) / 60_000);
  const days = Math.round((startOfDay(now) - startOfDay(date)) / DAY_MS);
  if (days <= 0) {
    if (minutes < 1) {
      return t('messages.messagesFormat.now');
    }
    return minutes < 60
      ? t('messages.messagesFormat.min', { minutes: minutes })
      : `${Math.floor(minutes / 60)}h`;
  }
  return days === 1 ? t('messages.messagesFormat.yesterday') : shortDate(date);
}

/** Day separator: "Today", "Yesterday" or "EEE, MMM d" (BR-12). */
export function dayLabel(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const days = Math.round((startOfDay(now) - startOfDay(date)) / DAY_MS);
  if (days === 0) {
    return t('messages.messagesFormat.today');
  }
  return days === 1
    ? t('messages.messagesFormat.yesterday')
    : date.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}

export function dayKey(iso: string): string {
  const date = new Date(iso);
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

/** "h:mm a" in the device zone and locale. */
export function timeLabel(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/** Fixed copy of a failed send by status (never the backend text). */
export function sendFailure(error: unknown): { bodyRejected: boolean; message: string } {
  if (isApiError(error) && error.status === 400) {
    return { bodyRejected: true, message: BODY_ERROR_MESSAGE() };
  }
  if (isApiError(error) && error.status === 429) {
    return { bodyRejected: false, message: RATE_LIMIT_MESSAGE() };
  }
  return { bodyRejected: false, message: t('messages.messagesFormat.couldntSendCheckYour') };
}
