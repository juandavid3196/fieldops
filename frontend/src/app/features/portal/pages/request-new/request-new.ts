import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Subscription, forkJoin } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { AvailabilityStep } from '../../../service-request/components/availability-step/availability-step';
import { PropertyStep } from '../../../service-request/components/property-step/property-step';
import { RequestStepper } from '../../../service-request/components/request-stepper/request-stepper';
import { RequestSummaryPanel } from '../../../service-request/components/request-summary-panel/request-summary-panel';
import { ReviewStep } from '../../../service-request/components/review-step/review-step';
import { ServiceDetailsStep } from '../../../service-request/components/service-details-step/service-details-step';
import { ServiceRequestForm } from '../../../service-request/models/service-request.model';
import { ServiceRequestWizardStore } from '../../../service-request/services/service-request-wizard.store';
import { fieldId } from '../../../service-request/service-request.validators';
import { PortalPropertiesService } from '../../services/portal-properties.service';
import { PortalRequestsService } from '../../services/portal-requests.service';

type LoadState = 'loading' | 'ready' | 'unavailable' | 'error';

export function unavailableText(phone: string | null): string {
  return phone === null
    ? "Online requests aren't available right now. Call the company."
    : `Online requests aren't available right now. Call ${phone}.`;
}

/** Whether the organization offers at least one requestable service (BR-28). */
export function hasRequestableService(form: ServiceRequestForm): boolean {
  return form.categories.some((category) => category.services.length > 0);
}

/**
 * New request (BR-28): the public wizard without the Contact step. The property step offers the
 * customer's properties (the selected one preselected) or a new one; the session supplies the rest.
 */
@Component({
  selector: 'app-portal-request-new',
  imports: [
    AvailabilityStep,
    ButtonDirective,
    Message,
    PropertyStep,
    RequestStepper,
    RequestSummaryPanel,
    ReviewStep,
    RouterLink,
    ServiceDetailsStep,
    Skeleton,
  ],
  providers: [ServiceRequestWizardStore],
  templateUrl: './request-new.html',
  styleUrl: './request-new.scss',
})
export class PortalRequestNew {
  private readonly requests = inject(PortalRequestsService);
  private readonly properties = inject(PortalPropertiesService);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private loading: Subscription | undefined;

  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly state = signal<LoadState>('loading');
  protected readonly unavailable = signal(unavailableText(null));
  protected readonly loadError = "We couldn't load the request form.";

  constructor() {
    this.load();

    effect(() => {
      const request = this.store.focusRequest();
      if (request === null) return;
      afterNextRender(
        () => {
          const selector =
            request.target === 'heading' ? '[data-sr-heading]' : `#${fieldId(request.target)}`;
          this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
        },
        { injector: this.injector },
      );
    });
  }

  protected retry(): void {
    this.load();
  }

  private load(): void {
    this.loading?.unsubscribe();
    this.state.set('loading');
    this.loading = forkJoin({ form: this.requests.form(), known: this.properties.list() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ form, known }) => {
          if (!hasRequestableService(form)) {
            this.unavailable.set(unavailableText(form.phone));
            this.state.set('unavailable');
            return;
          }
          const preselected =
            this.route.snapshot.queryParamMap.get('propertyId') ?? this.stateProperty();
          this.store.configurePortal(
            form,
            {
              properties: known.map((property) => ({
                id: property.id,
                name: property.name,
                addressLine1: property.addressLine1,
                city: property.city,
                stateRegion: property.stateRegion,
                postalCode: property.postalCode,
              })),
              submit: (payload, files) => this.requests.create(payload, files),
            },
            preselected,
          );
          this.state.set('ready');
        },
        error: (failure: unknown) => {
          if (isApiError(failure) && failure.status === 404) {
            this.unavailable.set(unavailableText(null));
            this.state.set('unavailable');
          } else {
            this.state.set('error');
          }
        },
      });
  }

  /** The selected property may also arrive as router state. */
  private stateProperty(): string | null {
    const value: unknown = (history.state as Record<string, unknown> | null)?.['propertyId'];
    return typeof value === 'string' ? value : null;
  }
}
