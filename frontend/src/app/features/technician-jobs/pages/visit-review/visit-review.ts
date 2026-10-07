import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { SessionService } from '../../../../core/services/session.service';
import { FORBIDDEN_MESSAGE } from '../../utils/technician-errors';

export const REVIEW_COMING_SOON = 'Review and completion is coming soon.';

/** BR-19 placeholder for Design 10 (`/today/visits/:visitId/review`); it reads and writes no data. */
@Component({
  selector: 'app-visit-review',
  imports: [RouterLink, ButtonDirective],
  templateUrl: './visit-review.html',
  styleUrl: './visit-review.scss',
})
export class VisitReview {
  /** Same in-component technician check as the job page; other roles see the forbidden state. */
  readonly allowed = inject(SessionService).session()?.role.code === 'technician';
  readonly jobLink = [
    '/today/visits',
    inject(ActivatedRoute).snapshot.paramMap.get('visitId') ?? '',
  ];
  readonly comingSoon = REVIEW_COMING_SOON;
  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
}
