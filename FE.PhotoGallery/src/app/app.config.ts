import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { jwtInterceptor } from './services/jwt.interceptor';
import { RuntimeConfigService } from './services/runtime-config.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // withInterceptors() is required for the functional jwtInterceptor to
    // actually run. Plain provideHttpClient() ignores both this channel AND
    // the legacy HTTP_INTERCEPTORS DI token — symptom: every authenticated
    // API call returns 401 with authHeaderPresent=False on the backend
    // because the Authorization header is silently never attached.
    provideHttpClient(withInterceptors([jwtInterceptor])),
    // Fetch public runtime config (e.g. Google OAuth ClientId) before bootstrap.
    // Reads from backend GET /api/config/public so values are env-var driven
    // and don't require an Angular rebuild to change.
    provideAppInitializer(() => inject(RuntimeConfigService).load())
  ]
};
