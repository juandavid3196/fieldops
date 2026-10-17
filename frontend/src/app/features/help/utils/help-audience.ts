import { inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { HelpAudience } from '../models/help.model';
import { helpBasePath } from './help-content';

/**
 * Audience and base path of the help route being rendered, read from the leaf route data
 * (`helpAudience`). The same pages serve `/help` (internal) and `/portal/help` (portal).
 */
export function injectHelpContext(): { readonly audience: HelpAudience; readonly base: string } {
  const audience: HelpAudience =
    inject(ActivatedRoute).snapshot.data['helpAudience'] === 'portal' ? 'portal' : 'internal';
  return { audience, base: helpBasePath(audience) };
}
