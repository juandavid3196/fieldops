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
import { Subscription, tap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { AvailabilityStep } from '../../components/availability-step/availability-step';
import { Confirmation } from '../../components/confirmation/confirmation';
import { ContactStep } from '../../components/contact-step/contact-step';
import { PropertyStep } from '../../components/property-step/property-step';
import { RequestStepper } from '../../components/request-stepper/request-stepper';
import { RequestSummaryPanel } from '../../components/request-summary-panel/request-summary-panel';
import { ReviewStep } from '../../components/review-step/review-step';
import { ServiceDetailsStep } from '../../components/service-details-step/service-details-step';
import { ServiceRequestWizardStore } from '../../services/service-request-wizard.store';
import { ServiceRequestService } from '../../services/service-request.service';
import { LOAD_ERROR_MESSAGE, UNAVAILABLE_MESSAGE } from '../../service-request.messages';
import { fieldId } from '../../service-request.validators';

type LoadState = 'loading' | 'ready' | 'unavailable' | 'error';

@Component({
  selector: 'app-service-request',
  imports: [
    RouterLink,
    ButtonDirective,
    Message,
    Skeleton,
    RequestStepper,
    RequestSummaryPanel,
    ContactStep,
    PropertyStep,
    ServiceDetailsStep,
    AvailabilityStep,
    ReviewStep,
    Confirmation,
  ],
  // The store is page-scoped: leaving the page discards all entered data (BR-20).
  providers: [ServiceRequestWizardStore],
  templateUrl: './service-request.html',
  styleUrl: './service-request.scss',
})
export class ServiceRequest {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ServiceRequestService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private slug = '';
  private loading: Subscription | undefined;

  protected readonly store = inject(ServiceRequestWizardStore);
  protected readonly state = signal<LoadState>('loading');
  protected readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  protected readonly unavailableMessage = UNAVAILABLE_MESSAGE;

  constructor() {
    this.route.paramMap
      .pipe(
        tap((params) => (this.slug = params.get('slug') ?? '')),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.load());

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
    this.loading = this.api
      .getForm(this.slug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (config) => {
          this.store.configure(this.slug, config);
          this.state.set('ready');
        },
        error: (failure: unknown) =>
          this.state.set(
            isApiError(failure) && failure.kind === 'not-found' ? 'unavailable' : 'error',
          ),
      });
  }
}
