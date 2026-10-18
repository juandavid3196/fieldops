export type ConversationType = 'job' | 'team';
export type MessageFilter = 'all' | 'unread' | 'jobs' | 'team';

export interface ConversationLastMessage {
  readonly preview: string;
  readonly authorName: string;
  readonly authorInitials: string;
  readonly isMine: boolean;
  readonly createdAt: string;
}

export interface ConversationSummary {
  /** `null` for a Team conversation not resolved yet (virtual entry). */
  readonly id: string | null;
  readonly type: ConversationType;
  readonly title: string;
  readonly branchId: string;
  readonly workOrder: { readonly id: string; readonly displayNumber: string } | null;
  readonly lastMessage: ConversationLastMessage | null;
  readonly unreadCount: number;
}

export interface ConversationPage {
  readonly items: readonly ConversationSummary[];
  readonly nextCursor: string | null;
}

export interface ConversationDetail {
  readonly id: string;
  readonly type: ConversationType;
  readonly title: string;
  readonly branchName: string;
  readonly workOrder: {
    readonly id: string;
    readonly displayNumber: string;
    readonly title: string;
    readonly visitId: string | null;
  } | null;
}

export interface ConversationMessage {
  readonly id: string;
  readonly clientMessageId: string | null;
  readonly author: {
    readonly name: string;
    readonly initials: string;
    readonly roleLabel: string;
  };
  readonly isMine: boolean;
  readonly body: string;
  readonly createdAt: string;
}

export interface MessagePage {
  readonly items: readonly ConversationMessage[];
  readonly hasMore: boolean;
}

export type ResolveRequest =
  | { readonly type: 'job'; readonly workOrderId: string }
  | { readonly type: 'team'; readonly branchId: string };
