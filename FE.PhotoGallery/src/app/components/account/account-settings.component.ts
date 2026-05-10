import { Component } from '@angular/core';

/**
 * Placeholder Account Settings page.
 *
 * The real content lands in issue #67 — this stub exists so the
 * `/account` route compiles after issue #60 wires BaseLayoutComponent
 * as the auth shell.
 */
@Component({
  selector: 'app-account-settings',
  standalone: true,
  imports: [],
  template: `
    <section class="account-settings" aria-labelledby="account-settings-title">
      <h1 id="account-settings-title">Account Settings</h1>
      <p>Coming soon.</p>
    </section>
  `,
  styles: [`
    .account-settings {
      padding: 24px;
      max-width: 1200px;
      margin: 0 auto;
    }
    h1 {
      margin: 0 0 12px;
      font-size: 24px;
    }
  `]
})
export class AccountSettingsComponent {}
