import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface User {
  id: string;
  email: string;
  firstName?: string;
  lastName?: string;
  roles: string[];
}

export interface AuthResponse {
  accessToken: string;
  idToken?: string;
  user: User;
}

/**
 * Service for handling authentication and JWT token management
 */
@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = `${environment.apiUrl}/api/auth`;
  private readonly TOKEN_KEY = 'access_token';
  private readonly USER_KEY = 'current_user';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getUserFromStorage());
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {}

  /**
   * Initiate Google OAuth login flow
   */
  loginWithGoogle(): void {
    window.location.href = `${this.API_URL}/login`;
  }

  /**
   * Handle Google OAuth callback
   */
  handleGoogleCallback(code: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.API_URL}/google-callback`, { code })
      .pipe(
        tap(response => {
          this.setToken(response.accessToken);
          this.setUser(response.user);
          this.currentUserSubject.next(response.user);
        })
      );
  }

  /**
   * Logout and clear token
   */
  logout(): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/logout`, {}).pipe(
      tap(() => {
        this.clearToken();
        this.clearUser();
        this.currentUserSubject.next(null);
      })
    );
  }

  /**
   * Get current user info from backend
   */
  getCurrentUser(): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/me`).pipe(
      tap((response: any) => {
        console.log('Auth service: Received response from /me endpoint:', response);
        // If we got an accessToken in the response, store it
        if (response.accessToken) {
          this.setToken(response.accessToken);
          console.log('Auth service: Token stored in localStorage');
        }
        const user = response.user || response;
        this.setUser(user);
        this.currentUserSubject.next(user);
        console.log('Auth service: User set in service:', user);
      })
    );
  }

  /**
   * Refresh access token
   */
  refreshToken(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/refresh`, {}).pipe(
      tap(response => {
        this.setToken(response.accessToken);
        this.setUser(response.user);
        this.currentUserSubject.next(response.user);
      })
    );
  }

  /**
   * Get stored JWT token
   */
  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  /**
   * Set JWT token
   */
  private setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  /**
   * Clear JWT token
   */
  private clearToken(): void {
    localStorage.removeItem(this.TOKEN_KEY);
  }

  /**
   * Get stored user
   */
  getUser(): User | null {
    return this.currentUserSubject.value;
  }

  /**
   * Get user from storage
   */
  private getUserFromStorage(): User | null {
    const stored = localStorage.getItem(this.USER_KEY);
    return stored ? JSON.parse(stored) : null;
  }

  /**
   * Set current user
   */
  private setUser(user: User): void {
    localStorage.setItem(this.USER_KEY, JSON.stringify(user));
  }

  /**
   * Clear user
   */
  private clearUser(): void {
    localStorage.removeItem(this.USER_KEY);
  }

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return this.getToken() !== null;
  }

  /**
   * Check if user has a specific role
   */
  hasRole(role: string): boolean {
    const user = this.getUser();
    return user ? user.roles.includes(role) : false;
  }

  /**
   * Check if user is admin
   */
  isAdmin(): boolean {
    return this.hasRole('Admin');
  }
}
