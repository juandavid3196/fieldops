import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import {
  TIME_WINDOW_LABELS,
  URGENCY_LABELS,
} from '../../../service-request/service-request.messages';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { RequestAvailability, RequestDetail } from '../../models/portal.model';
import { PortalRequestsService } from '../../services/portal-requests.service';
import { dateLabel } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';
import { quoteChip, requestChip } from '../../utils/portal-status';

export function availabilityText(availability: RequestAvailability | null): string {
  if (availability === null) {
    return '—';
  }
  const window = TIME_WINDOW_LABELS[availability.window];
  if (availability.mode === 'asap') {
    return `As soon as possible · ${window}`;
  }
  if (availability.mode === 'flexible') {
    return `I'm flexible · ${window}`;
  }
  const date = availability.dates[0];
  return date === undefined
    ? window
    : `${dateLabel(date.date)} · ${TIME_WINDOW_LABELS[date.window]}`;
}

/** Request detail (BR-27): no attachments, internal notes, messages or assignees. */
@Component({
  selector: 'app-portal-request-detail',
  imports: [PortalState, PortalStatusChip, RouterLink],
  templateUrl: './request-detail.html',
  styleUrls: ['../../portal.scss', '../../portal-facts.scss'],
})
export class PortalRequestDetail {
  private readonly requests = inject(PortalRequestsService);
  private readonly requestId = inject(ActivatedRoute).snapshot.paramMap.get('requestId') ?? '';

  readonly resource = new PortalResource<RequestDetail>();
  readonly view = computed(() => {
    const request = this.resource.data();
    if (request === null) {
      return null;
    }
    const property = request.property;
    return {
      request,
      chip: requestChip(request.status),
      submitted: dateLabel(request.submittedOn),
      urgency: URGENCY_LABELS[request.urgency as keyof typeof URGENCY_LABELS] ?? request.urgency,
      availability: availabilityText(request.availability),
      property:
        property === null
          ? '—'
          : `${property.name} — ${property.addressLine1}, ${property.city}, ${property.stateRegion} ${property.postalCode}`,
      quoteChip: request.quote === null ? null : quoteChip(request.quote.status),
    };
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.resource.load(this.requests.get(this.requestId));
  }
}
