import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { IdentityProviderType } from '../../services/auth/identity-provider';

/**
 * Login page component.
 *
 * Two paths from this page:
 *   1. Authenticated path — Sign in with Google (admin/owner experience)
 *   2. Unauthenticated guest path — Enter an access code to view a shared album
 *
 * The access-code input redirects to /code/{code} which is the public
 * gallery route handled by CodeGalleryComponent.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="login-container">
      <div class="login-card">
        <h1>Photo Gallery</h1>
        <p class="subtitle">Professional Photo Sharing Platform</p>

        <div id="google-signin-container" class="google-signin-container" (click)="signInGoogle()"></div>

        <p class="info-text">
          Sign in with your Google account to access your photo albums and share them with clients.
        </p>

        <div class="divider"><span>or</span></div>

        <div class="access-code-section" *ngIf="!showCodeInput">
          <p class="guest-text">Have an access code from a photographer?</p>
          <button class="access-code-btn" (click)="toggleCodeInput()" data-testid="enter-access-code-btn">
            Enter Access Code →
          </button>
        </div>

        <div class="access-code-form" *ngIf="showCodeInput">
          <label for="accessCode" class="code-label">Access Code</label>
          <input
            id="accessCode"
            #codeInput
            type="text"
            class="code-input"
            [(ngModel)]="accessCode"
            (keydown.enter)="goToCode()"
            placeholder="ABC123XYZ789"
            autocomplete="off"
            spellcheck="false"
            data-testid="access-code-input">
          <div *ngIf="codeError" class="code-error" data-testid="access-code-error">{{ codeError }}</div>
          <div class="code-actions">
            <button class="cancel-btn" (click)="cancelCode()">Cancel</button>
            <button
              class="go-btn"
              (click)="goToCode()"
              [disabled]="!accessCode.trim()"
              data-testid="access-code-submit">
              View Album
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .login-container {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Oxygen', 'Ubuntu', 'Cantarell', sans-serif;
    }

    .login-card {
      background: white;
      border-radius: 8px;
      padding: 48px 32px;
      box-shadow: 0 10px 40px rgba(0, 0, 0, 0.1);
      text-align: center;
      max-width: 400px;
      width: 100%;
    }

    h1 {
      margin: 0 0 8px 0;
      font-size: 32px;
      color: #333;
      font-weight: 600;
    }

    .subtitle {
      margin: 0 0 32px 0;
      color: #666;
      font-size: 14px;
    }

    .google-signin-container {
      display: flex;
      justify-content: center;
      min-height: 44px;
    }

    .info-text {
      margin-top: 24px;
      color: #999;
      font-size: 13px;
      line-height: 1.5;
    }

    .divider {
      position: relative;
      margin: 28px 0 20px;
      color: #aaa;
      font-size: 13px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .divider::before,
    .divider::after {
      content: '';
      position: absolute;
      top: 50%;
      width: calc(50% - 24px);
      height: 1px;
      background: #e0e0e0;
    }

    .divider::before { left: 0; }
    .divider::after { right: 0; }

    .divider span {
      background: white;
      padding: 0 10px;
    }

    .access-code-section .guest-text {
      margin: 0 0 12px 0;
      color: #666;
      font-size: 14px;
    }

    .access-code-btn {
      width: 100%;
      padding: 12px 24px;
      background: white;
      border: 1px solid #0066cc;
      border-radius: 6px;
      font-size: 15px;
      font-weight: 500;
      color: #0066cc;
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .access-code-btn:hover {
      background: #e3f2fd;
    }

    .access-code-form {
      text-align: left;
      margin-top: 8px;
    }

    .code-label {
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: #666;
      margin-bottom: 6px;
    }

    .code-input {
      width: 100%;
      padding: 10px 12px;
      border: 1px solid #ccc;
      border-radius: 6px;
      font-size: 16px;
      font-family: 'Courier New', monospace;
      letter-spacing: 0.05em;
      box-sizing: border-box;
      text-transform: uppercase;
    }

    .code-input:focus {
      outline: none;
      border-color: #0066cc;
      box-shadow: 0 0 0 3px rgba(0, 102, 204, 0.1);
    }

    .code-error {
      color: #c33;
      font-size: 13px;
      margin-top: 8px;
    }

    .code-actions {
      display: flex;
      gap: 8px;
      margin-top: 14px;
    }

    .cancel-btn {
      flex: 1;
      padding: 10px;
      background: white;
      border: 1px solid #ccc;
      border-radius: 6px;
      font-size: 14px;
      cursor: pointer;
      color: #666;
    }

    .cancel-btn:hover { background: #f5f5f5; }

    .go-btn {
      flex: 2;
      padding: 10px;
      background: #0066cc;
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .go-btn:hover:not(:disabled) {
      background: #0052a3;
    }

    .go-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  `]
})
export class LoginComponent implements OnInit {
  showCodeInput = false;
  accessCode = '';
  codeError = '';

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    // Build marker — easy way to confirm the latest FE bundle is loaded
    // when troubleshooting auth issues. If you don't see this in the
    // browser console, ng serve is running a stale bundle (restart it).
    console.info('[LoginComponent] ngOnInit — auth flow build: 2026-05-09 v3');

    // If already authenticated, redirect to dashboard.
    if (this.authService.isAuthenticatedSync()) {
      this.router.navigate(['/dashboard']);
      return;
    }

    // Mount Google's official Sign-in button via GIS once the DOM is ready,
    // then begin awaiting the credential. The GIS button click opens the
    // popup (handled inside its own iframe); the GIS callback fires on
    // success and resolves the promise that signIn() awaits, which then
    // POSTs the idToken to /api/auth/external-login.
    //
    // Eager-await is necessary because clicks INSIDE the cross-origin GIS
    // iframe don't bubble to our wrapper's (click) handler. We also keep the
    // template's (click)="signInGoogle()" handler as a fallback for clicks
    // that hit the wrapper's padding (matches the VerdantIQ pattern).
    setTimeout(async () => {
      await this.authService.renderProviderButton(
        IdentityProviderType.Google,
        'google-signin-container'
      );
      this.beginGoogleSignIn();
    }, 0);
  }

  /**
   * Click handler bound to the wrapper div around the GIS-rendered button.
   * Mirrors VerdantIQ's pattern. Kicks off the same eager-await pipeline
   * that ngOnInit started, so a click that lands on the wrapper's padding
   * (rather than the iframe button) still drives sign-in.
   */
  signInGoogle(): void {
    console.info('[LoginComponent] signInGoogle clicked');
    this.beginGoogleSignIn();
  }

  /**
   * Idempotent — multiple awaiters share the same promise inside
   * GoogleAuthService, so calling this from both ngOnInit and the click
   * handler is safe.
   */
  private async beginGoogleSignIn(): Promise<void> {
    try {
      const success = await this.authService.googleSignIn();
      if (success) {
        console.info('[LoginComponent] googleSignIn returned true — redirecting to /dashboard');
        this.router.navigate(['/dashboard']);
      } else {
        console.warn('[LoginComponent] googleSignIn returned false');
      }
    } catch (err) {
      console.error('[LoginComponent] googleSignIn threw', err);
    }
  }

  toggleCodeInput(): void {
    this.showCodeInput = true;
    this.codeError = '';
  }

  cancelCode(): void {
    this.showCodeInput = false;
    this.accessCode = '';
    this.codeError = '';
  }

  goToCode(): void {
    const code = this.accessCode.trim().toUpperCase();
    if (!code) {
      this.codeError = 'Please enter an access code.';
      return;
    }
    // Basic shape: alphanumeric only — server validates the actual code
    if (!/^[A-Z0-9-]{4,32}$/.test(code)) {
      this.codeError = 'Access codes are alphanumeric (4-32 characters).';
      return;
    }
    this.router.navigate(['/code', code]);
  }
}
