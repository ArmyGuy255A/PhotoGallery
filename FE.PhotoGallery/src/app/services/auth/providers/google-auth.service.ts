import { Injectable, inject } from '@angular/core';
import { IdentityProvider, IdentityProviderType } from '../identity-provider';
import { RuntimeConfigService } from '../../runtime-config.service';

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

  token: string;
  private tokenReadyResolver: ((token: string) => void) | null = null;
  private tokenReadyPromise: Promise<string>;
  private gisInitialized = false;

  constructor() {
    this.token = '';

    this.tokenReadyPromise = new Promise(resolve => {
      this.tokenReadyResolver = resolve;
    });
  }

  /**
   * Lazy-init Google Identity Services on first use. The clientId comes from
   * the backend at runtime via RuntimeConfigService — APP_INITIALIZER guarantees
   * it's loaded before any user-driven sign-in flow can fire.
   */
  private ensureGisInitialized(): void {
    if (this.gisInitialized) return;
    const clientId = this.runtimeConfig.googleClientId;
    if (!clientId) {
      console.error('GoogleAuthService: googleClientId is empty — check backend Google:ClientId config');
      return;
    }
    google.accounts.id.initialize({
      client_id: clientId,
      callback: (response: any) => {
        this.token = response.credential;
        this.tokenReadyResolver?.(this.token);
      },
      ux_mode: 'popup',
    });
    this.gisInitialized = true;
  }

  async signIn(): Promise<string> {
    this.ensureGisInitialized();
    if (this.token) {
      return this.token;
    }

    return this.tokenReadyPromise;
  }

  private async handleCredentialResponse(response: any): Promise<void> {

    const idToken = response.credential;
    console.log('ID Token:', idToken);
    if (idToken) {
      this.token = idToken;
    } else {
      console.error('No ID token received from Google');
      this.token = '';
    }


    this.status = GoogleAuthStatus.SignedIn;
    return idToken;
  }

  printStatus(): void {
    console.log(`GoogleAuthService status: ${GoogleAuthStatus[this.status]}`);
  }

  signOut(): Promise<void> {
    return Promise.resolve(google.accounts.id.disableAutoSelect());
  }

  refresh(): Promise<string> {
    // Google Identity Services does not expose client-side token refresh
    return Promise.reject('Refresh not supported for Google');
  }

  validate(token: string): boolean {
    return !!token; // Placeholder — you might verify token expiration later
  }

  renderButton(containerId: string): void {
    const button = document.getElementById(containerId);
    if (!button) return;

    this.ensureGisInitialized();

    google.accounts.id.renderButton(button, {
      type: 'text',
      shape: 'square',
      theme: 'outline',
      text: 'signin_with',
      size: 'large',

    });

    button.addEventListener('click', () => {
      google.accounts.id.prompt();
    });

  }
}
