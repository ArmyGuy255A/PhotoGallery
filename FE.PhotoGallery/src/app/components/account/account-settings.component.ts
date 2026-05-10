import { Component } from '@angular/core';
import { BackToDashboardComponent } from '../back-to-dashboard/back-to-dashboard.component';

/**
 * Account Settings page — MVP placeholder.
 *
 * Real profile editing (name, email, password, avatar) is tracked in
 * issue #69. For now this is a friendly stub directing users to their
 * administrator. The route compiles under BaseLayoutComponent so the
 * navbar/sidenav shell renders around it (issue #60 / PR #93).
 */
@Component({
  selector: 'app-account-settings',
  standalone: true,
  imports: [BackToDashboardComponent],
  template: `
    <div class="account-settings" data-testid="account-settings-page">
      <header>
        <app-back-to-dashboard></app-back-to-dashboard>
        <h1>Account Settings</h1>
        <p class="subtitle">Manage your profile and preferences.</p>
      </header>

      <section class="coming-soon" aria-live="polite">
        <p>Profile editing is coming soon.</p>
        <p class="hint">
          For now, contact your administrator to update your name, email,
          or password.
        </p>
      </section>
    </div>
  `,
  styles: [`
    .account-settings { max-width: 1200px; margin: 0 auto; padding: 24px; }
    header { margin-bottom: 24px; }
    h1 { margin: 8px 0 4px 0; font-size: 26px; color: #333; }
    .subtitle { color: #666; margin: 0; }
    .coming-soon {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 32px 24px;
      text-align: center;
      color: #555;
    }
    .coming-soon p { margin: 4px 0; }
    .coming-soon .hint { font-size: 13px; color: #888; margin-top: 8px; }
  `]
})
export class AccountSettingsComponent {}
