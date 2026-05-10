import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { RuntimeConfigService } from '../../runtime-config.service';
import { IdentityProvider, IdentityProviderType } from '../identity-provider';

declare const google: any;

export enum GoogleAuthStatus {
  Idle,
  SigningIn,
  SignedIn,
}

@Injectable({
  providedIn: 'root'
})

export class GoogleAuthService implements IdentityProvider {
  readonly name = IdentityProviderType.Google;
  readonly containerId = 'google-signin';
  public status: GoogleAuthStatus = GoogleAuthStatus.Idle;

  private readonly runtimeConfig = inject(RuntimeConfigService);
  private readonly debugEnabled = !environment.production;

  token: string;
  private tokenReadyResolver: ((token: string) => void) | null = null;
  private tokenReadyPromise: Promise<string>;
  private gisInitialized = false;
  private gisReadyPromise: Promise<void> | null = null;

  constructor() {
    this.debug('constructor');
    this.token = '';

    this.tokenReadyPromise = new Promise(resolve => {
      this.tokenReadyResolver = resolve;
    });
  }

  private debug(message: string, details?: Record<string, unknown>): void {
    if (!this.debugEnabled) return;
    if (details) {
      console.log('[GoogleAuthService]', message, details);
      return;
    }
    console.log('[GoogleAuthService]', message);
  }

  private tokenMeta(token: string): { tokenPresent: boolean; tokenLength: number } {
    return {
      tokenPresent: !!token,
      tokenLength: token?.length ?? 0,
    };
  }

  /**
   * Resolves once the Google Identity Services SDK script (loaded async/defer
   * from index.html) has finished evaluating and exposed the global `google`
   * object. Polls every 50ms up to 10s; rejects after that to prevent UI hangs.
   */
  private waitForGis(): Promise<void> {
    if (this.gisReadyPromise) return this.gisReadyPromise;
    this.debug('waitForGis:start');
    this.gisReadyPromise = new Promise<void>((resolve, reject) => {
      const start = Date.now();
      const tick = () => {
        if (typeof (window as any).google !== 'undefined' && (window as any).google?.accounts?.id) {
          this.debug('waitForGis:success', { elapsedMs: Date.now() - start });
          resolve();
          return;
        }
        if (Date.now() - start > 10000) {
          this.debug('waitForGis:timeout', { elapsedMs: Date.now() - start });
          reject(new Error('Google Identity Services SDK failed to load within 10s'));
          return;
        }
        setTimeout(tick, 50);
      };
      tick();
    });
    return this.gisReadyPromise;
  }

  /**
   * Lazy-init Google Identity Services on first use. The clientId comes from
   * the backend at runtime via RuntimeConfigService — APP_INITIALIZER guarantees
   * it's loaded before any user-driven sign-in flow can fire.
   */
  private async ensureGisInitialized(): Promise<void> {
    this.debug('ensureGisInitialized:entry', { gisInitialized: this.gisInitialized });
    if (this.gisInitialized) return;
    const clientId = this.runtimeConfig.googleClientId;
    this.debug('ensureGisInitialized:clientIdCheck', {
      hasClientId: !!clientId,
      clientIdLength: clientId?.length ?? 0,
    });
    if (!clientId) {
      console.error(
        'GoogleAuthService: googleClientId is empty. ' +
        'Set Google:ClientId in the backend (appsettings or Google__ClientId env var) and restart it. ' +
        'See Documentation/Architecture/AUTHENTICATION.md → Google Cloud Console Setup.'
      );
      return;
    }
    await this.waitForGis();
    google.accounts.id.initialize({
      client_id: clientId,
      callback: (response: any) => {
        this.debug('gisCallback:received', {
          credentialPresent: !!response?.credential,
          credentialLength: response?.credential?.length ?? 0,
        });
        this.token = response.credential;
        this.debug('gisCallback:tokenStored', this.tokenMeta(this.token));
        this.tokenReadyResolver?.(this.token);
      },
      ux_mode: 'popup',
      // FedCM bypasses the legacy postMessage popup channel, which
      // browsers block when the opener page's COOP is "same-origin".
      // The dev server (angular.json) and production responses also
      // set Cross-Origin-Opener-Policy: same-origin-allow-popups so
      // older browsers without FedCM still work.
      use_fedcm_for_prompt: true,
    });
    // Logged once at first sign-in attempt so devs can verify the (origin, clientId)
    // pair against Google Cloud Console's Authorized JavaScript origins list when
    // the popup fails with "no registered origin".
    // See Documentation/Architecture/AUTHENTICATION.md → "Still seeing Error 401".
    console.info(`[GIS init] origin=${window.location.origin} clientId=${clientId}`);
    this.gisInitialized = true;
    this.debug('ensureGisInitialized:done');
  }

  async signIn(): Promise<string> {
    this.debug('signIn:entry', this.tokenMeta(this.token));
    await this.ensureGisInitialized();
    this.debug('signIn:afterEnsure', { gisInitialized: this.gisInitialized });
    if (this.token) {
      this.debug('signIn:returnExistingToken', this.tokenMeta(this.token));
      return this.token;
    }

    this.debug('signIn:waitingForCallback');
    return this.tokenReadyPromise;
  }

  private async handleCredentialResponse(response: any): Promise<void> {

    const idToken = response.credential;
    this.debug('handleCredentialResponse:received', {
      credentialPresent: !!idToken,
      credentialLength: idToken?.length ?? 0,
    });
    if (idToken) {
      this.token = idToken;
      this.debug('handleCredentialResponse:tokenStored', this.tokenMeta(this.token));
    } else {
      console.error('No ID token received from Google');
      this.token = '';
      this.debug('handleCredentialResponse:tokenMissing');
    }


    this.status = GoogleAuthStatus.SignedIn;
    return idToken;
  }

  printStatus(): void {
    this.debug('printStatus', { status: GoogleAuthStatus[this.status] });
  }

  signOut(): Promise<void> {
    this.debug('signOut:called');
    return Promise.resolve(google.accounts.id.disableAutoSelect());
  }

  refresh(): Promise<string> {
    // Google Identity Services does not expose client-side token refresh
    return Promise.reject('Refresh not supported for Google');
  }

  validate(token: string): boolean {
    return !!token; // Placeholder — you might verify token expiration later
  }

  async renderButton(containerId: string): Promise<void> {
    const button = document.getElementById(containerId);
    if (!button) {
      this.debug('renderButton:missingContainer', { containerId });
      return;
    }

    await this.ensureGisInitialized();
    if (!this.gisInitialized) return; // ensureGisInitialized logged the reason

    google.accounts.id.renderButton(button, {
      type: 'text',
      shape: 'square',
      theme: 'outline',
      text: 'signin_with',
      size: 'large',
    });
    this.debug('renderButton:success', { containerId });
  }
}
