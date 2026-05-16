import { ApplicationConfig, ErrorHandler, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { jwtInterceptor } from './services/jwt.interceptor';
import { RuntimeConfigService } from './services/runtime-config.service';
import { AuthService } from './services/auth.service';
import { CartService } from './services/cart.service';
import { AppInsightsErrorHandler, AppInsightsService } from './services/app-insights.service';

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
    // Fetch public runtime config (e.g. Google OAuth ClientId, Application
    // Insights connection string) before bootstrap. Reads from backend
    // GET /api/config/public so values are env-var driven and don't require
    // an Angular rebuild to change.
    provideAppInitializer(() => inject(RuntimeConfigService).load()),
    // Initialise Application Insights right after runtime config loads.
    // No-op when the backend returns an empty connection string (local dev).
    provideAppInitializer(() => inject(AppInsightsService).initialise()),
    // Route uncaught Angular errors into App Insights as exceptions.
    { provide: ErrorHandler, useClass: AppInsightsErrorHandler },
    // Fire-and-forget: when the user is authenticated at boot, hydrate the
    // server-backed cart in the background so the navbar badge / drawer have
    // accurate state on first paint. Errors are logged but do NOT block
    // bootstrap — the cart drawer will simply show empty until the next add.
    provideAppInitializer(() => {
      const auth = inject(AuthService);
      const cart = inject(CartService);
      if (auth.isAuthenticatedSync()) {
        // Mint a fresh app token straight from the DB so role changes
        // applied by an admin since the last login take effect on the
        // next page refresh (no logout/login dance). Fire-and-forget;
        // boot proceeds with the current token and the UI re-renders
        // when the refresh resolves a few hundred ms later.
        auth.refreshRolesFromServer()
          .catch(err => console.warn('AuthService.refreshRolesFromServer failed at boot', err));

        cart.loadForUser().catch(err => console.error('CartService.loadForUser failed at boot', err));
      }
    })
  ]
};
