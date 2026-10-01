import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { SkillsAvailabilityPage } from '../pages/skills-availability/skills-availability';

/** Delegates to the page's dirty confirmation; a cleared session (sign-out) leaves without prompting. */
export const skillsAvailabilityUnsavedChangesGuard: CanDeactivateFn<SkillsAvailabilityPage> = (
  component,
) => (inject(SessionService).session() === null ? true : component.canLeave());
