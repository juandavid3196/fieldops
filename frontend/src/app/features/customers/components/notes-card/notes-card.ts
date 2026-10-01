import { Component, computed, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';

import { NoteItem, RegionState } from '../../models/customer.model';
import { formatDateTime } from '../../utils/customer-detail-format';

export const NOTE_INPUT_ID = 'customer-detail-note';

/**
 * Internal notes card (BR-16): pinned note, newest-first entries with Show more and, for mutation
 * roles, the append-only input. Presentational: the page owns the requests.
 */
@Component({
  selector: 'app-notes-card',
  imports: [FormsModule, ButtonDirective, Message, Skeleton, SpinnerIcon, Textarea],
  templateUrl: './notes-card.html',
  styleUrl: './notes-card.scss',
})
export class NotesCard {
  readonly pinnedNote = input<string | null>(null);
  readonly state = input.required<RegionState<readonly NoteItem[]>>();
  readonly timezone = input('UTC');
  readonly hasMore = input(false);
  readonly loadingMore = input(false);
  readonly canMutate = input(false);
  readonly adding = input(false);
  readonly noteError = input<string | null>(null);
  /** The pending text of the note input. */
  readonly text = model('');

  readonly editRequested = output<void>();
  readonly addRequested = output<void>();
  readonly showMore = output<void>();
  readonly retry = output<void>();

  readonly inputId = NOTE_INPUT_ID;
  readonly errorId = `${NOTE_INPUT_ID}-error`;

  readonly entries = computed(() =>
    (this.state().data ?? []).map((note) => ({
      id: note.id,
      note: note.note,
      author: note.authorName,
      date: formatDateTime(note.createdAt, this.timezone()),
    })),
  );
}
