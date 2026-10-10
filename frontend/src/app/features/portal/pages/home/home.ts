import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Observable, catchError, of, switchMap, throwError } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { PortalSessionService } from '../../../../core/services/portal-session.service';
import {
  PublicQuoteDialog,
  PublicQuoteDialogKind,
  publicTextError,
} from '../../../quotes/components/public-quote-dialog/public-quote-dialog';
import { PUBLIC_RATE_LIMITED, RESPONSE_ERROR } from '../../../quotes/models/public-quote.model';
import { ActionQuoteCard } from '../../components/action-quote-card/action-quote-card';
import { ActiveRequestsCard } from '../../components/active-requests-card/active-requests-card';
import { ActivityCard } from '../../components/activity-card/activity-card';
import { AppointmentCard } from '../../components/appointment-card/appointment-card';
import { HelpCard } from '../../components/help-card/help-card';
import { PaymentDueCard } from '../../components/payment-due-card/payment-due-card';
import { PortalCardSkeleton } from '../../components/portal-card-skeleton/portal-card-skeleton';
import { PortalMessageDialog } from '../../components/portal-message-dialog/portal-message-dialog';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalRescheduleDialog } from '../../components/portal-reschedule-dialog/portal-reschedule-dialog';
import { PropertyCard } from '../../components/property-card/property-card';
import { UpdatesCard } from '../../components/updates-card/updates-card';
import { Dashboard, DashboardProperty, PortalAppointment } from '../../models/portal.model';
import { PortalDashboardService } from '../../services/portal-dashboard.service';
import { PortalQuotesService } from '../../services/portal-quote.service';
import { greeting } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';

export const DASHBOARD_ERROR_MESSAGE = "We couldn't load your dashboard.";
export const ASK_SENT_MESSAGE = 'Question sent';

/**
 * Home dashboard (BR-17 … BR-26). One `GET /portal/dashboard` feeds every card. Selector state
 * lives only in this page: absent `propertyId` means All; the default (primary, else first
 * property) is applied once after the first load unless the contact already chose.
 */
@Component({
  selector: 'app-portal-home',
  imports: [
    ActionQuoteCard,
    ActiveRequestsCard,
    ActivityCard,
    AppointmentCard,
    ButtonDirective,
    HelpCard,
    Message,
    PaymentDueCard,
    PortalCardSkeleton,
    PortalMessageDialog,
    PortalPropertyFilter,
    PortalRescheduleDialog,
    PropertyCard,
    PublicQuoteDialog,
    RouterLink,
    UpdatesCard,
  ],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class PortalHome {
  private readonly dashboards = inject(PortalDashboardService);
  private readonly quotes = inject(PortalQuotesService);
  private readonly sessions = inject(PortalSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private defaultApplied = false;
  private trigger: HTMLElement | null = null;

  readonly resource = new PortalResource<Dashboard>();
  /** Selected property id; `null` is All (no `propertyId` is sent). */
  readonly selected = signal<string | null>(null);
  private readonly chosen = signal(false);
  readonly knownProperties = signal<readonly DashboardProperty[]>([]);

  readonly askOpen = signal(false);
  readonly askSubmitting = signal(false);
  readonly askFieldError = signal<string | null>(null);
  readonly askFailure = signal<string | null>(null);
  readonly rescheduleTarget = signal<PortalAppointment | null>(null);
  readonly messageOpen = signal(false);

  readonly errorMessage = DASHBOARD_ERROR_MESSAGE;
  readonly loading = computed(() => this.resource.state() === 'loading');
  readonly failed = computed(
    () => this.resource.state() === 'error' || this.resource.state() === 'not-found',
  );
  readonly data = this.resource.data;
  readonly firstName = computed(() => this.sessions.session()?.user.firstName ?? '');
  readonly greeting = computed(() => greeting(new Date(), this.firstName()));
  readonly showAll = computed(() => this.knownProperties().length >= 2);
  readonly selectedProperty = computed<DashboardProperty | null>(() => {
    const properties = this.data()?.properties ?? [];
    const selected = this.selected();
    return (
      properties.find((property) => property.id === selected) ??
      properties.find((property) => property.isPrimary) ??
      properties[0] ??
      null
    );
  });
  readonly requestQuery = computed(() =>
    this.selected() === null ? {} : { propertyId: this.selected() },
  );
  readonly dialogKind = computed<PublicQuoteDialogKind | null>(() =>
    this.askOpen() ? 'ask' : null,
  );

  private readonly contactId = computed(() => this.sessions.session()?.account.contactId);
  private readonly fragment = toSignal(this.route.fragment, { initialValue: null });

  constructor() {
    // Loads once per account; switching accounts resets the selector (BR-07).
    effect(() => {
      if (this.contactId() === undefined) {
        return;
      }
      untracked(() => {
        this.selected.set(null);
        this.chosen.set(false);
        this.defaultApplied = false;
        this.knownProperties.set([]);
        this.reload();
      });
    });

    // "Help" lands on the Need help card once the content is rendered (BR-16).
    effect(() => {
      if (this.fragment() === 'help' && !this.loading() && !this.failed()) {
        afterNextRender(
          () => {
            const help = this.host.nativeElement.querySelector<HTMLElement>('#help');
            help?.scrollIntoView?.({ block: 'start' });
            help?.focus();
          },
          { injector: this.injector },
        );
      }
    });
  }

  reload(): void {
    this.resource.load(this.fetch(this.selected(), !this.defaultApplied && !this.chosen()));
  }

  select(propertyId: string | null): void {
    this.chosen.set(true);
    this.selected.set(propertyId);
    this.resource.load(this.fetch(propertyId, false));
  }

  private fetch(propertyId: string | null, applyDefault: boolean): Observable<Dashboard> {
    return this.dashboards.dashboard(propertyId).pipe(
      switchMap((dashboard) => {
        const pick =
          dashboard.properties.find((property) => property.isPrimary) ?? dashboard.properties[0];
        if (applyDefault && propertyId === null && pick !== undefined) {
          // First load without a choice: reload once scoped to the default property.
          this.selected.set(pick.id);
          this.knownProperties.set(dashboard.properties);
          return this.dashboards.dashboard(pick.id);
        }
        return of(dashboard);
      }),
      switchMap((dashboard) => {
        this.defaultApplied = true;
        this.knownProperties.set(dashboard.properties);
        return of(dashboard);
      }),
      catchError((error: unknown) => {
        if (isApiError(error) && error.status === 404 && this.selected() !== null) {
          // The selected property is gone: back to the default and reload (BR-18).
          this.selected.set(null);
          this.chosen.set(false);
          this.defaultApplied = false;
          return this.fetch(null, true);
        }
        return throwError(() => error);
      }),
    );
  }

  // Ask a question (BR-20, BR-30)

  openAsk(): void {
    const active = this.document.activeElement;
    this.trigger = active instanceof HTMLElement ? active : null;
    this.askFieldError.set(null);
    this.askFailure.set(null);
    this.askOpen.set(true);
  }

  closeAsk(): void {
    if (this.askSubmitting()) {
      return;
    }
    this.askOpen.set(false);
    this.restoreFocus();
  }

  submitAsk(text: string): void {
    const quote = this.data()?.actionQuote ?? null;
    if (quote === null || this.askSubmitting()) {
      return;
    }
    this.askSubmitting.set(true);
    this.askFieldError.set(null);
    this.askFailure.set(null);
    this.quotes
      .askQuestion(quote.id, text)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.askSubmitting.set(false);
          this.askOpen.set(false);
          this.resource.patch((dashboard) =>
            dashboard.actionQuote === null
              ? dashboard
              : {
                  ...dashboard,
                  actionQuote: { ...dashboard.actionQuote, status: 'clarification_requested' },
                },
          );
          this.restoreFocus();
        },
        error: (error: unknown) => this.askFailed(error, text),
      });
  }

  private askFailed(error: unknown, text: string): void {
    this.askSubmitting.set(false);
    const apiError = isApiError(error) ? error : null;
    if (apiError?.status === 404 || apiError?.status === 409) {
      // Answered, expired or gone: the card is stale, so close and reload it.
      this.askOpen.set(false);
      this.reload();
    } else if (apiError?.kind === 'validation') {
      this.askFieldError.set(publicTextError('ask', text) ?? RESPONSE_ERROR);
    } else if (apiError?.kind === 'rate-limited') {
      this.askFailure.set(PUBLIC_RATE_LIMITED);
    } else {
      this.askFailure.set(RESPONSE_ERROR);
    }
  }

  // Reschedule (BR-32)

  openReschedule(appointment: PortalAppointment): void {
    this.rescheduleTarget.set(appointment);
  }

  closeReschedule(): void {
    this.rescheduleTarget.set(null);
  }

  rescheduled(event: { readonly visitId: string; readonly requestedOn: string }): void {
    this.rescheduleTarget.set(null);
    this.resource.patch((dashboard) =>
      dashboard.upcomingAppointment?.visitId === event.visitId
        ? {
            ...dashboard,
            upcomingAppointment: {
              ...dashboard.upcomingAppointment,
              rescheduleRequestedOn: event.requestedOn,
              canRequestReschedule: false,
            },
          }
        : dashboard,
    );
  }

  // Message (BR-35)

  openMessage(): void {
    this.messageOpen.set(true);
  }

  closeMessage(): void {
    this.messageOpen.set(false);
  }

  messagingUnavailable(): void {
    this.messageOpen.set(false);
    this.resource.patch((dashboard) => ({
      ...dashboard,
      organization: { ...dashboard.organization, canReceiveMessages: false },
    }));
  }

  private restoreFocus(): void {
    const trigger = this.trigger;
    this.trigger = null;
    afterNextRender(() => trigger?.focus(), { injector: this.injector });
  }
}
