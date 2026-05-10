import {Component, computed, inject, signal, WritableSignal} from '@angular/core';
import {MatListModule} from '@angular/material/list';
import {MatSidenavModule} from '@angular/material/sidenav';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {CommonModule} from '@angular/common';
import {GlobalStateService} from '../../services/global-state.service';
import {RouterLink, RouterLinkActive, RouterOutlet} from '@angular/router';
import {AuthService} from '../../services/auth.service';

/**
 * One entry in the authenticated sidebar nav.
 *
 * `requiresAdmin` gates the entry to users carrying the Admin role
 * (covers issue #102 admin-only Admin Settings link).
 */
export interface SidebarNavItem {
  label: string;
  routerLink: string;
  requiresAdmin?: boolean;
}

@Component({
  selector: 'app-sidenav',
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatSidenavModule, MatListModule,
    RouterLink, RouterLinkActive, RouterOutlet,
  ],
  templateUrl: './sidenav.component.html',
  styleUrl: './sidenav.component.scss'
})
export class SidenavComponent {
  globalStateService: GlobalStateService = inject(GlobalStateService);
  private readonly authService: AuthService = inject(AuthService);

  /**
   * Title-Case-rendered nav items (issue #105). `requiresAdmin` filters
   * the Admin Settings link off the sidebar for non-admin users
   * (issue #102 acceptance criterion).
   */
  readonly navItems: readonly SidebarNavItem[] = [
    { label: 'Dashboard',        routerLink: '/dashboard' },
    { label: 'Shared Albums',    routerLink: '/shared-albums' },
    { label: 'Account Settings', routerLink: '/account' },
    { label: 'Admin Settings',   routerLink: '/admin/settings', requiresAdmin: true },
  ];

  isMobile() : WritableSignal<boolean> {
    return this.globalStateService.isMobile;
  }

  /** Visible items for the current user — admin entries hidden when not admin. */
  visibleNavItems(): SidebarNavItem[] {
    const isAdmin = this.authService.isAdmin();
    return this.navItems.filter(i => !i.requiresAdmin || isAdmin);
  }

  /**
   * On mobile, auto-close the sidenav when the user picks a destination.
   * Desktop users keep the drawer open between clicks (matches typical
   * shell-app expectations).
   */
  onNavigate(): void {
    if (this.isMobile()()) {
      this.globalStateService.closeSidebar();
    }
  }

  close() {
    this.globalStateService.closeSidebar();
  }

  toggle() {
    this.globalStateService.toggleSidebar();
  }

  open() {
    this.globalStateService.openSidebar();
  }
}
