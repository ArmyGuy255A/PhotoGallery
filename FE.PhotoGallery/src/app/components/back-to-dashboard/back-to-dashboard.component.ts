import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

/**
 * Modular Back-to-Dashboard control (issue #106).
 *
 * Adopted across every authenticated subpage so the back affordance
 * lives in the same place with the same styling — top-left of the page
 * header, immediately before the page title. The optional `[label]`
 * input lets future call sites point at non-Dashboard back-targets
 * without forking the component.
 */
@Component({
  selector: 'app-back-to-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <a class="back-to-dashboard"
       [routerLink]="routerLink"
       data-testid="back-to-dashboard">
      ← {{ label }}
    </a>
  `,
  styles: [`
    .back-to-dashboard {
      display: inline-flex;
      align-items: center;
      color: #0066cc;
      text-decoration: none;
      font-size: 14px;
      font-weight: 500;
      padding: 8px 12px;
      border-radius: 6px;
      transition: background 0.15s;
    }
    .back-to-dashboard:hover,
    .back-to-dashboard:focus-visible {
      background: #e3f2fd;
      text-decoration: underline;
      outline: none;
    }
  `]
})
export class BackToDashboardComponent {
  /** Destination route. Defaults to the authenticated dashboard. */
  @Input() routerLink: string = '/dashboard';
  /** Visible label. Defaults to "Back to Dashboard". */
  @Input() label: string = 'Back to Dashboard';
}
