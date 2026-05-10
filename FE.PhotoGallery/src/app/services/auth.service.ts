import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../environments/environment';
import { GoogleAuthService } from './auth/providers/google-auth.service';
import { IdentityProvider, IdentityProviderType } from './auth/identity-provider';
import { IdentityUser } from './auth/identity-user';

export enum TokenType {
  IdpToken = 'idpToken',
  AppToken = 'appToken'
}

/**
 * Lightweight user shape derived from the decoded AppToken JWT.
 * Kept for compatibility with components that consumed the previous
 * `User` interface from this module.
 */
export interface User {
  id: string;
  email: string;
  firstName?: string;
  lastName?: string;
  roles: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly providers = new Map<IdentityProviderType, IdentityProvider>();

  private isRefreshingToken = false;
  private refreshTokenPromise: Promise<void> | null = null;

  /**
   * In-flight signIn promise, keyed by provider type. Prevents the SPA from
   * firing two concurrent /api/auth/external-login POSTs when both
   * LoginComponent's eager-await (ngOnInit) and click handler call signIn()
   * for the same login. Without this, the backend's HandleExternalLoginAsync
   * processes two parallel requests for the same account, which races on
   * UserManager state and previously surfaced as ``ConcurrencyFailure:
   * Optimistic concurrency failure, object has been modified.``
   */
  private inFlightSignIn = new Map<IdentityProviderType, Promise<boolean>>();

  private isAuthenticatedSubject = new BehaviorSubject<boolean>(false);
  public isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

  private currentUserSubject = new BehaviorSubject<User | null>(this.decodeAppTokenUser());
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(
    private http: HttpClient,
    private googleProvider: GoogleAuthService
  ) {
    this.providers.set(googleProvider.name, googleProvider);
  }

  async googleSignIn(): Promise<boolean> {
    return await this.signIn(IdentityProviderType.Google);
  }

  async signIn(providerType: IdentityProviderType): Promise<boolean> {
    if (!providerType) {
      console.error('No IDP issuer found, cannot sign in.');
      return false;
    }
    const provider = this.providers.get(providerType);
    if (!provider) throw new Error(`Unknown provider: ${providerType}`);

    // Dedupe: if a signIn for this provider is already in flight, return that
    // promise instead of starting a second one. Both LoginComponent.ngOnInit's
    // eager-await and the (click)="signInGoogle()" handler call this; without
    // dedupe they each fire a separate POST and race on the backend.
    const existing = this.inFlightSignIn.get(providerType);
    if (existing) {
      console.info('[AuthService] signIn already in flight — returning shared promise');
      return existing;
    }

    const flow = (async () => {
      try {
        const idToken = await provider.signIn();
        return await this.signInWithToken(idToken, providerType);
      } finally {
        this.inFlightSignIn.delete(providerType);
      }
    })();
    this.inFlightSignIn.set(providerType, flow);
    return flow;
  }

  async signInWithToken(token: string, providerType: IdentityProviderType = IdentityProviderType.Google): Promise<boolean> {
    this.setToken(TokenType.IdpToken, token);
    const idpToken = this.getToken(TokenType.IdpToken);

    try {
      const result: any = await this.http
        .post(`${environment.apiUrl}/api/auth/external-login`, {
          provider: providerType,
          idToken: idpToken
        })
        .toPromise();

      const appToken = result?.token;
      if (!appToken) {
        console.error('No JWT received from server');
        return false;
      }

      this.setToken(TokenType.AppToken, appToken);
      this.currentUserSubject.next(this.decodeAppTokenUser());
      this.isAuthenticatedSubject.next(true);
      return true;
    } catch (err) {
      console.error('Authentication failed:', err);
      return false;
    }
  }

  renderProviderButton(providerType: IdentityProviderType, containerId: string): void {
    const provider = this.providers.get(providerType);
    provider?.renderButton(containerId);
  }

  getProviders(): IdentityProvider[] {
    return Array.from(this.providers.values());
  }

  getToken(type: TokenType): string | null {
    const jwt = localStorage.getItem(type);
    if (!jwt) {
      return null;
    }
    return jwt;
  }

  setToken(type: TokenType, token: string): void {
    localStorage.setItem(type, token);
  }

  deleteToken(type: TokenType): void {
    localStorage.removeItem(type);
  }

  /**
   * Logs out the user by clearing both tokens and resetting state.
   * Client-side only — there is no server-side session to revoke in
   * the GIS popup flow.
   */
  logout(): void {
    this.deleteToken(TokenType.AppToken);
    this.deleteToken(TokenType.IdpToken);
    this.currentUserSubject.next(null);
    this.isAuthenticatedSubject.next(false);
  }

  /**
   * Signs out of the IdP (Google) and clears the IdP token.
   */
  async signOut(): Promise<void> {
    const provider = this.providers.get(IdentityProviderType.Google);
    if (provider) {
      try { await provider.signOut(); } catch { /* swallow — best-effort */ }
    }
    this.deleteToken(TokenType.IdpToken);
  }

  async refreshAppToken(): Promise<void> {
    if (this.isRefreshingToken) {
      return this.refreshTokenPromise!;
    }

    this.isRefreshingToken = true;
    this.refreshTokenPromise = new Promise(async (resolve) => {
      try {
        const idpToken = this.getToken(TokenType.IdpToken);
        if (idpToken) {
          await this.signInWithToken(idpToken);
        }
      } catch (error) {
        console.error('Failed to refresh app token:', error);
        this.logout();
      } finally {
        this.isRefreshingToken = false;
        this.refreshTokenPromise = null;
        resolve();
      }
    });

    return this.refreshTokenPromise;
  }

  tokenLifetimeIsValid(type: TokenType): boolean {
    const jwt = this.getToken(type);
    if (!jwt) {
      return false;
    }
    const decodedToken = this.decodeJwt(jwt);
    if (!decodedToken || !decodedToken.exp) {
      return false;
    }
    const currentTime = Math.floor(Date.now() / 1000);
    const isValid = decodedToken.exp > currentTime;

    if (!isValid && type === TokenType.AppToken) {
      this.deleteToken(TokenType.AppToken);
    }

    return isValid;
  }

  /**
   * Async: validates app + IdP tokens, refreshing the app token from the
   * IdP token if needed. Use this in flows that can await.
   */
  async isAuthenticated(): Promise<boolean> {
    const appToken = this.getToken(TokenType.AppToken);
    const idpToken = this.getToken(TokenType.IdpToken);

    if (!idpToken || !this.tokenLifetimeIsValid(TokenType.IdpToken)) {
      await this.signOut();
      return false;
    }

    if (!appToken || !this.tokenLifetimeIsValid(TokenType.AppToken)) {
      await this.refreshAppToken();
      const refreshedToken = this.getToken(TokenType.AppToken);
      return !!refreshedToken && this.tokenLifetimeIsValid(TokenType.AppToken);
    }

    return true;
  }

  /**
   * Synchronous best-effort check used in places that can't await
   * (route guards, ngOnInit pre-navigation checks, template *ngIfs).
   * Returns true iff a non-expired AppToken is in localStorage.
   */
  isAuthenticatedSync(): boolean {
    return this.tokenLifetimeIsValid(TokenType.AppToken);
  }

  async updateAuthenticationState(): Promise<void> {
    const result = await this.isAuthenticated();
    this.isAuthenticatedSubject.next(result);
    this.currentUserSubject.next(this.decodeAppTokenUser());
  }

  /** Returns the User decoded from the AppToken, or null if unavailable. */
  getUser(): User | null {
    return this.currentUserSubject.value;
  }

  hasRole(role: string): boolean {
    const user = this.getUser();
    return !!user && Array.isArray(user.roles) && user.roles.includes(role);
  }

  isAdmin(): boolean {
    return this.hasRole('Admin');
  }

  private decodeJwt(token: string): IdentityUser | null {
    try {
      const payload = token.split('.')[1];
      const decoded = atob(payload);
      return JSON.parse(decoded);
    } catch (error) {
      console.error('Invalid JWT:', error);
      return null;
    }
  }

  /** Decode the AppToken into a User (best-effort; null when missing/invalid). */
  private decodeAppTokenUser(): User | null {
    const appToken = localStorage.getItem(TokenType.AppToken);
    if (!appToken) return null;
    const decoded = this.decodeJwt(appToken) as any;
    if (!decoded) return null;

    const rolesRaw = decoded.role ?? decoded.roles ?? [];
    const roles = Array.isArray(rolesRaw) ? rolesRaw : [rolesRaw];

    return {
      id: decoded.sub ?? '',
      email: decoded.email ?? '',
      firstName: decoded.given_name ?? decoded.firstName,
      lastName: decoded.family_name ?? decoded.lastName,
      roles
    };
  }

  private getIdpIssuer(): IdentityProviderType | null {
    const idpToken = this.getToken(TokenType.IdpToken);
    if (!idpToken) {
      return null;
    }

    try {
      const payload = idpToken.split('.')[1];
      const decoded = JSON.parse(atob(payload));
      switch (decoded.iss) {
        case 'https://accounts.google.com':
        case 'accounts.google.com':
          return IdentityProviderType.Google;
        case 'https://login.microsoftonline.com':
          return IdentityProviderType.Microsoft;
        case 'https://www.facebook.com':
          return IdentityProviderType.Facebook;
        case 'https://appleid.apple.com':
          return IdentityProviderType.Apple;
        default:
          return null;
      }
    } catch (error) {
      console.error('Invalid JWT:', error);
      return null;
    }
  }
}
