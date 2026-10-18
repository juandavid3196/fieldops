import { isApiError } from '../../../core/models/api-error.model';
import { ConversationSummary } from '../models/messages.model';

export const MAX_BODY_LENGTH = 2000;
export const COUNTER_FROM = 1800;
export const SEARCH_MIN_LENGTH = 2;

export const LIST_ERROR_MESSAGE = "We couldn't load your messages. Try again.";
export const CONVERSATION_ERROR_MESSAGE = "We couldn't load this conversation. Try again.";
export const NOT_AVAILABLE_MESSAGE = "This conversation isn't available.";
export const OPEN_ERROR_MESSAGE = "We couldn't open the conversation. Try again.";
export const FORBIDDEN_MESSAGE = "You don't have access to Messages.";
export const BODY_ERROR_MESSAGE = 'Enter a message of up to 2000 characters.';
export const RATE_LIMIT_MESSAGE = "You're sending messages too quickly. Try again in a moment.";

/** Roles with the Messaging policy (UX only; the backend decides). */
export const MESSAGING_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'technician',
];

export function unreadText(count: number): string {
  return `${count} unread ${count === 1 ? 'message' : 'messages'}`;
}

/** "<first name>: <text>", "You: <text>" or "No messages yet" (BR-16). */
export function previewLine(conversation: ConversationSummary): string {
  const last = conversation.lastMessage;
  if (last === null) {
    return 'No messages yet';
  }
  const who = last.isMine ? 'You' : (last.authorName.trim().split(/\s+/)[0] ?? '');
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
      return 'Now';
    }
    return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)}h`;
  }
  return days === 1 ? 'Yesterday' : shortDate(date);
}

/** Day separator: "Today", "Yesterday" or "EEE, MMM d" (BR-12). */
export function dayLabel(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const days = Math.round((startOfDay(now) - startOfDay(date)) / DAY_MS);
  if (days === 0) {
    return 'Today';
  }
  return days === 1
    ? 'Yesterday'
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
    return { bodyRejected: true, message: BODY_ERROR_MESSAGE };
  }
  if (isApiError(error) && error.status === 429) {
    return { bodyRejected: false, message: RATE_LIMIT_MESSAGE };
  }
  return { bodyRejected: false, message: "Couldn't send. Check your connection." };
}
