import { Routes, CanMatchFn } from '@angular/router';
import { inject } from '@angular/core';
import { LoginComponent } from './components/login/login.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { AlbumsCreateComponent } from './components/albums/albums-create.component';
import { AlbumDetailComponent } from './components/albums/album-detail.component';
import { AlbumEditComponent } from './components/albums/album-edit.component';
import { CodeGalleryComponent } from './components/code-gallery/code-gallery.component';
import { SharedAlbumsComponent } from './components/shared-albums/shared-albums.component';
import { AccountSettingsComponent } from './components/account/account-settings.component';
import { AdminSettingsComponent } from './components/admin/admin-settings.component';
import { BaseLayoutComponent } from './base-layout/base-layout.component';
import { AuthService } from './services/auth.service';
import { authGuard, adminGuard } from './services/auth.guard';

/**
 * canMatch helper for the public, unwrapped `/code/:code` route. Returns
 * `true` only when the visitor is NOT authenticated — authenticated
 * visitors fall through to the BaseLayoutComponent-wrapped variant below
 * (issue #99) so they see the global navbar + cart drawer.
 */
const anonymousOnlyMatch: CanMatchFn = () => !inject(AuthService).isAuthenticatedSync();

export const routes: Routes = [
  // Public routes — rendered without the BaseLayoutComponent shell.
  {
    path: 'login',
    component: LoginComponent
  },
  {
    // Public, chrome-less access-code route (anonymous viewers only).
    // The CartPanel + download telemetry on this path stay intact.
    // Authenticated users skip this match and fall through to the
    // BaseLayoutComponent-wrapped child route below — see issue #99.
    path: 'code/:code',
    component: CodeGalleryComponent,
    canMatch: [anonymousOnlyMatch]
  },

  // Authenticated app shell — every child route renders inside
  // <app-navbar> + <app-sidenav> + <app-footer> via BaseLayoutComponent.
  {
    path: '',
    component: BaseLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'albums/create', component: AlbumsCreateComponent, canActivate: [adminGuard] },
      { path: 'albums/:id/edit', component: AlbumEditComponent, canActivate: [adminGuard] },
      { path: 'albums/:id', component: AlbumDetailComponent },
      { path: 'shared-albums', component: SharedAlbumsComponent },
      // Authenticated viewers of /code/:code render through BaseLayoutComponent
      // so the global navbar (cart button + user dropdown) and global cart
      // drawer are visible — issue #99.
      { path: 'code/:code', component: CodeGalleryComponent },
      // Stub Account Settings — real content lands in issue #67.
      { path: 'account', component: AccountSettingsComponent },
      // Stub Admin Settings — real content lands in issue #70.
      { path: 'admin/settings', component: AdminSettingsComponent, canActivate: [adminGuard] },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // Catch-all → /dashboard (redirect, never clobbers BaseLayoutComponent).
  {
    path: '**',
    redirectTo: '/dashboard'
  }
];

