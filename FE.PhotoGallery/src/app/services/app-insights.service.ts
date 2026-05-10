import { ErrorHandler, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { AngularPlugin } from '@microsoft/applicationinsights-angularplugin-js';
import { RuntimeConfigService } from './runtime-config.service';

/**
 * Application Insights integration for the SPA.
 *
 * Initialized once at app bootstrap (see app.config.ts provideAppInitializer)
 * after RuntimeConfigService.load() resolves, so we have the connection
 * string from the backend's /api/config/public endpoint and don't need to
 * embed it in the Angular bundle.
 *
 * Captures:
 *   * Page views (router-aware via AngularPlugin)
 *   * Uncaught errors (registered as ErrorHandler in app.config.ts)
 *   * Unhandled promise rejections, fetch/XHR dependencies, console.error
 *
 * No-ops in local dev where the backend returns an empty connection string,
 * so `ng serve` still works without an Azure resource.
 */
@Injectable({ providedIn: 'root' })
export class AppInsightsService {
  private readonly runtimeConfig = inject(RuntimeConfigService);
  private readonly router = inject(Router);
  private readonly angularPlugin = new AngularPlugin();
  private _appInsights: ApplicationInsights | null = null;

  /** Idempotent — initialise() can be called multiple times safely. */
  initialise(): void {
    if (this._appInsights) return;

    const connectionString = this.runtimeConfig.applicationInsightsConnectionString;
    if (!connectionString) {
      console.info('[AppInsights] no connection string from /api/config/public — telemetry disabled');
      return;
    }

    this._appInsights = new ApplicationInsights({
      config: {
        connectionString,
        enableAutoRouteTracking: false,
        extensions: [this.angularPlugin],
        extensionConfig: {
          [this.angularPlugin.identifier]: { router: this.router, errorServices: [] }
        },
        // Capture unhandled exceptions, console.error, fetch/XHR deps.
        autoTrackPageVisitTime: true,
        disableFetchTracking: false,
        enableCorsCorrelation: true
      }
    });
    this._appInsights.loadAppInsights();
    this._appInsights.trackPageView();
  }

  trackException(error: Error): void {
    this._appInsights?.trackException({ exception: error });
  }
}

/**
 * Angular ErrorHandler that forwards uncaught errors to App Insights and then
 * delegates to the default handler so the dev console still shows the trace.
 */
@Injectable({ providedIn: 'root' })
export class AppInsightsErrorHandler implements ErrorHandler {
  private readonly ai = inject(AppInsightsService);

  handleError(error: any): void {
    const err = error instanceof Error ? error : new Error(String(error));
    this.ai.trackException(err);
    console.error(err);
  }
}
