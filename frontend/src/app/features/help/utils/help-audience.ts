import { inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { HelpAudience } from '../models/help.model';
import { helpBasePath } from './help-content';

/**
 * Audience and base path of the help route being rendered, read from the leaf route data
 * (`helpAudience`). The same pages serve `/help` (internal) and `/portal/help` (portal); the
 * public `/support` copy of the internal center overrides the base with `helpBase`.
 */
export function injectHelpContext(): { readonly audience: HelpAudience; readonly base: string } {
  const data = inject(ActivatedRoute).snapshot.data;
  const audience: HelpAudience = data['helpAudience'] === 'portal' ? 'portal' : 'internal';
  const base = typeof data['helpBase'] === 'string' ? data['helpBase'] : helpBasePath(audience);
  return { audience, base };
}
