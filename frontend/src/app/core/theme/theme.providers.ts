import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';

import { ThemeService } from './theme.service';

/** Takes over from the `index.html` script at start-up (BR-04) with the same resolution. */
export function provideTheme(): EnvironmentProviders {
  return makeEnvironmentProviders([provideAppInitializer(() => inject(ThemeService).init())]);
}
