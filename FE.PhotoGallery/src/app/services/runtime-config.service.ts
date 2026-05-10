import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

/**
 * Public, browser-safe runtime config fetched once at app startup from
 * the backend's GET /api/config/public endpoint.
 *
 * Wired into bootstrap via provideAppInitializer in app.config.ts so any
 * service (e.g. GoogleAuthService) can read these values synchronously
 * after Angular's initialization phase completes.
 *
 * Single source of truth = backend ConfigurationSettings (Google:ClientId,
 * etc). Changing the value only requires a backend env var or appsettings
 * update — no Angular rebuild needed.
 */
export interface PublicConfig {
  googleClientId: string;
  applicationInsightsConnectionString: string;
}

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly http = inject(HttpClient);
  private config: PublicConfig | null = null;

  /** Called by APP_INITIALIZER. Fails open with empty values so the app
   *  can still render an error UI rather than refusing to bootstrap. */
  async load(): Promise<void> {
    try {
      const url = `${environment.apiUrl}/api/config/public`;
      this.config = await firstValueFrom(this.http.get<PublicConfig>(url));
    } catch (err) {
      console.error('RuntimeConfigService: failed to load /api/config/public', err);
      this.config = { googleClientId: '', applicationInsightsConnectionString: '' };
    }
  }

  get googleClientId(): string {
    if (!this.config) {
      // Should not happen — APP_INITIALIZER awaits load() before bootstrap.
      console.warn('RuntimeConfigService accessed before load() resolved');
      return '';
    }
    return this.config.googleClientId;
  }

  /** Application Insights connection string for browser-side telemetry.
   *  Empty in local dev where the backend isn't wired to an AI resource. */
  get applicationInsightsConnectionString(): string {
    return this.config?.applicationInsightsConnectionString ?? '';
  }
}
