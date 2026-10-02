import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import {
  BOARD_STATUSES,
  BoardStatus,
  COLUMN_LABELS,
  ColumnState,
  RequestCard,
  STATUS_LABELS,
  URGENCY_LABELS,
} from '../../models/requests.model';
import { ageLabel, avatarLabel, cardDate, cardName } from '../../utils/requests-format';

const SKELETON_CARDS = [0, 1, 2];

/**
 * Four status columns of cards (BR-04, BR-05). Presentational: below md the columns stack as a
 * vertical list grouped under their headings; from md the board scrolls horizontally.
 */
@Component({
  selector: 'app-request-board',
  imports: [ButtonDirective, Skeleton],
  templateUrl: './request-board.html',
  styleUrl: './request-board.scss',
})
export class RequestBoard {
  readonly columns = input.required<Readonly<Record<BoardStatus, ColumnState>>>();
  /** First load: skeleton cards instead of content. */
  readonly loading = input(false);
  /** Reload with content kept in place. */
  readonly refreshing = input(false);
  readonly selectedId = input<string | null>(null);
  readonly timezone = input('UTC');

  readonly cardOpened = output<string>();
  readonly loadMore = output<BoardStatus>();

  readonly statuses = BOARD_STATUSES;
  readonly skeletonCards = SKELETON_CARDS;
  readonly columnLabels = COLUMN_LABELS;

  readonly urgencyLabel = (card: RequestCard): string => URGENCY_LABELS[card.urgency];
  readonly showsUrgency = (card: RequestCard): boolean =>
    card.urgency === 'urgent' || card.urgency === 'emergency';
  readonly name = (card: RequestCard, status: BoardStatus): string => cardName(card, status);
  readonly statusLabel = (status: BoardStatus): string => STATUS_LABELS[status];
  readonly date = (card: RequestCard): string => cardDate(card, this.timezone());
  readonly age = (card: RequestCard): string => ageLabel(card.createdAt, this.timezone());
  readonly avatarName = avatarLabel;
}
