import { Component } from '@angular/core';

/**
 * Placeholder Admin Settings page.
 *
 * The real configuration UI (site-wide settings, user management,
 * role assignment) lands in issue #70 — this stub exists so the
 * `/admin/settings` route compiles and the user-dropdown entry has
 * a destination.
 */
@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [],
  template: `
    <section
      class="admin-settings"
      data-testid="admin-settings-page"
      aria-labelledby="admin-settings-title"
    >
      <h1 id="admin-settings-title">Admin Settings</h1>
      <section class="coming-soon">
        <p>Admin configuration is coming soon.</p>
        <p class="hint">
          Site-wide settings, user management, and role assignment will live here.
        </p>
      </section>
    </section>
  `,
  styles: [`
    .admin-settings {
      padding: 24px;
      max-width: 1200px;
      margin: 0 auto;
    }
    h1 {
      margin: 0 0 12px;
      font-size: 24px;
    }
    .coming-soon p {
      margin: 0 0 8px;
      color: #555;
    }
    .coming-soon .hint {
      font-size: 13px;
      color: #888;
    }
  `]
})
export class AdminSettingsComponent {}
