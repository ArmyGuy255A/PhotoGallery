import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

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

        <button class="google-login-btn" (click)="loginWithGoogle()">
          <svg class="google-icon" viewBox="0 0 24 24">
            <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"/>
            <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"/>
            <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"/>
            <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"/>
          </svg>
          Sign in with Google
        </button>

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

    .google-login-btn {
      width: 100%;
      padding: 12px 24px;
      background: white;
      border: 1px solid #ddd;
      border-radius: 6px;
      font-size: 16px;
      font-weight: 500;
      color: #333;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      transition: all 0.3s ease;
    }

    .google-login-btn:hover {
      background: #f9f9f9;
      border-color: #ccc;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    .google-icon {
      width: 20px;
      height: 20px;
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
    // If already authenticated, redirect to dashboard
    if (this.authService.isAuthenticated()) {
      this.router.navigate(['/dashboard']);
    }
  }

  loginWithGoogle(): void {
    this.authService.loginWithGoogle();
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
