import { AfterViewInit, Injectable } from '@angular/core';
import { IdentityProvider, IdentityProviderType } from '../identity-provider';
import { environment } from '../../../../environments/environment';

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

  token: string;
  private tokenReadyResolver: ((token: string) => void) | null = null;
  private tokenReadyPromise: Promise<string>;

  constructor() {
    this.token = '';

    this.tokenReadyPromise = new Promise(resolve => {
      this.tokenReadyResolver = resolve;
    });

    google.accounts.id.initialize({
      client_id: environment.googleClientId,
      callback: (response: any) => {
        this.token = response.credential;

        // Resolve the pending promise
        this.tokenReadyResolver?.(this.token);
      },
      ux_mode: 'popup',
    });
  }

  async signIn(): Promise<string> {
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
