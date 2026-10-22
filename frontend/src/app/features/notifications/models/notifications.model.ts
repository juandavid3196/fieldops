import { t } from '../../../core/i18n/translate';
/** Fixed copy of the notifications center (specs/notifications-center/spec.md; backend text is never shown). */
export const NOTIFICATIONS_LOAD_ERROR_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationsLoadErrorMessage');
export const NOTIFICATIONS_FORBIDDEN_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationsForbiddenMessage');
export const NOTIFICATIONS_EMPTY_TITLE = (): string =>
  t('notifications.notificationsModel.notificationsEmptyTitle');
export const NOTIFICATIONS_EMPTY_TEXT = (): string =>
  t('notifications.notificationsModel.notificationsEmptyText');
export const NOTIFICATIONS_FILTERED_EMPTY_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationsFilteredEmptyMessage');
export const NOTIFICATION_UNAVAILABLE_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationUnavailableMessage');
export const NOTIFICATION_UPDATE_FAILED_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationUpdateFailedMessage');
export const NOTIFICATION_TARGET_GONE_MESSAGE = (): string =>
  t('notifications.notificationsModel.notificationTargetGoneMessage');

/** BR-13: rows per page offered by the UI (the first is the default). */
export const NOTIFICATIONS_PAGE_SIZES: readonly number[] = [10, 25, 50];
/** BR-20: URL parameters owned by the page. */
export const NOTIFICATIONS_QUERY_KEYS = [
  'tab',
  'q',
  'type',
  'branch',
  'date',
  'page',
  'pageSize',
] as const;
export const MAX_SEARCH_LENGTH = 100;

export type NotificationsTab = 'all' | 'unread';
export type NotificationType =
  'request' | 'message' | 'quote' | 'visit' | 'job' | 'invoice' | 'payment';
export type NotificationsDate = 'any' | 'today' | '7d' | '30d';
export type NotificationEntityKind =
  'request' | 'customer' | 'quote' | 'visit' | 'work_order' | 'invoice';

export interface NotificationTarget {
  readonly kind: NotificationEntityKind;
  readonly id: string;
}

export interface NotificationItem {
  readonly id: string;
  readonly type: NotificationType;
  readonly eventCode: string;
  readonly title: string;
  readonly description: string;
  readonly entityKind: NotificationEntityKind;
  readonly entityLabel: string;
  readonly secondaryLabel: string | null;
  readonly branch: { readonly id: string; readonly name: string } | null;
  readonly createdAt: string;
  readonly isRead: boolean;
  readonly target: NotificationTarget | null;
}

export interface NotificationsSummary {
  readonly unread: number;
  readonly today: number;
}

export interface NotificationsPage {
  readonly items: readonly NotificationItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly counts: { readonly all: number; readonly unread: number };
  readonly timeZone: string;
}

export interface NotificationsOptions {
  readonly branches: readonly { readonly id: string; readonly name: string }[];
}

export interface NotificationsView {
  readonly tab: NotificationsTab;
  readonly q: string;
  readonly type: NotificationType | null;
  readonly branch: string | null;
  readonly date: NotificationsDate;
  readonly page: number;
  readonly pageSize: number;
}

export interface NotificationsRequest {
  readonly tab: NotificationsTab;
  readonly q: string;
  readonly type: NotificationType | null;
  readonly branchId: string | null;
  readonly date: NotificationsDate;
  readonly page: number;
  readonly pageSize: number;
}

export interface ReadResult {
  readonly summary: NotificationsSummary;
}

export interface ReadAllResult extends ReadResult {
  readonly updated: number;
}
