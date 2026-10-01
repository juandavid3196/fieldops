import {
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Subject, catchError, debounceTime, map, of, switchMap, tap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { LinkableAccount } from '../../models/team.model';
import { TeamService } from '../../services/team.service';

export const LINK_CONFLICT_MESSAGE = "This user account can't be linked to this profile.";
export const LINK_FAILED_MESSAGE = "We couldn't link the user account. Try again.";
export const NO_ACCOUNTS_MESSAGE = 'No eligible user accounts.';
const SEARCH_DEBOUNCE_MS = 300;

/** BR-18 link dialog: searchable eligible members, single selection, inline 409. */
@Component({
  selector: 'app-link-account-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, InputText, Message, SpinnerIcon],
  templateUrl: './link-account-dialog.html',
  styleUrl: './link-account-dialog.scss',
})
export class LinkAccountDialog {
  private readonly team = inject(TeamService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searches = new Subject<string>();

  readonly visible = model(false);
  readonly technicianId = input<string | null>(null);
  readonly technicianName = input('');

  readonly linked = output<string>();
  readonly unauthorized = output<void>();

  readonly noAccountsMessage = NO_ACCOUNTS_MESSAGE;
  readonly search = signal('');
  readonly accounts = signal<readonly LinkableAccount[]>([]);
  readonly searching = signal(false);
  readonly loadFailed = signal(false);
  readonly selectedId = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly conflict = signal(false);

  constructor() {
    this.searches
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        tap(() => this.searching.set(true)),
        switchMap((term) =>
          this.team.linkableAccounts(term).pipe(
            map((accounts) => ({ accounts, failed: false })),
            catchError((error: unknown) => {
              if (isApiError(error) && error.kind === 'unauthorized') {
                this.unauthorized.emit();
              }
              return of({ accounts: [] as LinkableAccount[], failed: true });
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.searching.set(false);
        this.loadFailed.set(result.failed);
        this.accounts.set(result.accounts);
        if (!result.accounts.some((account) => account.organizationUserId === this.selectedId())) {
          this.selectedId.set(null);
        }
      });

    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });
  }

  private reset(): void {
    this.search.set('');
    this.accounts.set([]);
    this.selectedId.set(null);
    this.conflict.set(false);
    this.loadFailed.set(false);
    this.submitting.set(false);
    this.searching.set(true);
    this.team
      .linkableAccounts('')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accounts) => {
          this.searching.set(false);
          this.accounts.set(accounts);
        },
        error: (error: unknown) => {
          this.searching.set(false);
          this.loadFailed.set(true);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.unauthorized.emit();
          }
        },
      });
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.searches.next(value);
  }

  select(account: LinkableAccount): void {
    this.selectedId.set(account.organizationUserId);
    this.conflict.set(false);
  }

  close(): void {
    if (!this.submitting()) {
      this.visible.set(false);
    }
  }

  confirm(): void {
    const id = this.technicianId();
    const accountId = this.selectedId();
    if (id === null || accountId === null || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.conflict.set(false);
    this.team
      .link(id, accountId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.visible.set(false);
          this.messageService.add({ severity: 'success', summary: 'User account linked.' });
          this.linked.emit(id);
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          const kind = isApiError(error) ? error.kind : null;
          if (kind === 'unauthorized') {
            this.unauthorized.emit();
          } else if (kind === 'conflict') {
            this.conflict.set(true);
          } else {
            this.messageService.add({ severity: 'error', summary: LINK_FAILED_MESSAGE });
          }
        },
      });
  }

  readonly conflictMessage = LINK_CONFLICT_MESSAGE;
}
