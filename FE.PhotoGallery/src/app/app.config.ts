import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, HTTP_INTERCEPTORS } from '@angular/common/http';

import { routes } from './app.routes';
import { JwtInterceptor } from './services/jwt.interceptor';
import { RuntimeConfigService } from './services/runtime-config.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(),
    { provide: HTTP_INTERCEPTORS, useClass: JwtInterceptor, multi: true },
    // Fetch public runtime config (e.g. Google OAuth ClientId) before bootstrap.
    // Reads from backend GET /api/config/public so values are env-var driven
    // and don't require an Angular rebuild to change.
    provideAppInitializer(() => inject(RuntimeConfigService).load())
  ]
};
