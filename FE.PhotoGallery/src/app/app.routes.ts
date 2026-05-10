import { Routes } from '@angular/router';
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
import { authGuard, adminGuard } from './services/auth.guard';

export const routes: Routes = [
  // Public routes — rendered without the BaseLayoutComponent shell.
  {
    path: 'login',
    component: LoginComponent
  },
  {
    // Public access-code route per requirement #4 — no auth guard, no shell.
    // Anonymous viewers must not see the authenticated nav/sidenav.
    path: 'code/:code',
    component: CodeGalleryComponent
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

