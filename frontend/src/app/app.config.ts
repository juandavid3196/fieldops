import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { RouteReuseStrategy, provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { primeNgConfig } from './core/config/primeng.config';
import { provideI18n } from './core/i18n/i18n.providers';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { portalUnauthorizedInterceptor } from './core/interceptors/portal-unauthorized.interceptor';
import { ReloadRouteReuseStrategy } from './core/services/reload-route-reuse.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    { provide: RouteReuseStrategy, useClass: ReloadRouteReuseStrategy },
    provideHttpClient(
      withInterceptors([authInterceptor, portalUnauthorizedInterceptor, errorInterceptor]),
    ),
    providePrimeNG(primeNgConfig),
    provideI18n(),
  ],
};
