import { Route } from '@angular/router';

import { routes } from './app.routes';
import { LoginComponent } from './components/login/login.component';
import { CodeGalleryComponent } from './components/code-gallery/code-gallery.component';
import { BaseLayoutComponent } from './base-layout/base-layout.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { AlbumsCreateComponent } from './components/albums/albums-create.component';
import { AlbumDetailComponent } from './components/albums/album-detail.component';
import { AlbumEditComponent } from './components/albums/album-edit.component';
import { SharedAlbumsComponent } from './components/shared-albums/shared-albums.component';
import { AccountSettingsComponent } from './components/account/account-settings.component';
import { AdminSettingsComponent } from './components/admin/admin-settings.component';
import { authGuard, adminGuard, albumCreatorGuard } from './services/auth.guard';

function findTopLevel(path: string): Route | undefined {
  return routes.find(r => r.path === path);
}

function findShellRoute(): Route {
  const shell = routes.find(r => r.path === '' && r.component === BaseLayoutComponent);
  if (!shell) {
    throw new Error('BaseLayoutComponent shell route not found');
  }
  return shell;
}

function findChild(path: string): Route {
  const shell = findShellRoute();
  const child = (shell.children ?? []).find(r => r.path === path);
  if (!child) {
    throw new Error(`Child route ${path} not found under BaseLayoutComponent shell`);
  }
  return child;
}

describe('app.routes', () => {
  it('mounts /login outside the BaseLayoutComponent shell', () => {
    const login = findTopLevel('login');
    expect(login).toBeTruthy();
    expect(login!.component).toBe(LoginComponent);
  });

  it('mounts /code/:code outside the BaseLayoutComponent shell (anonymous viewers)', () => {
    const code = findTopLevel('code/:code');
    expect(code).toBeTruthy();
    expect(code!.component).toBe(CodeGalleryComponent);
    expect(code!.canActivate ?? []).not.toContain(authGuard);
    // Issue #99: the public, chrome-less route is gated by canMatch so it
    // only matches for unauthenticated viewers; authenticated viewers fall
    // through to the BaseLayoutComponent-wrapped child route.
    expect(Array.isArray(code!.canMatch) && code!.canMatch.length > 0).toBeTrue();
  });

  it('also mounts /code/:code as a BaseLayoutComponent child for authed viewers (issue #99)', () => {
    const code = findChild('code/:code');
    expect(code.component).toBe(CodeGalleryComponent);
  });

  it('uses BaseLayoutComponent as the parent for authenticated routes, guarded by authGuard', () => {
    const shell = findShellRoute();
    expect(shell.component).toBe(BaseLayoutComponent);
    expect(shell.canActivate).toContain(authGuard);
    expect(shell.children?.length).toBeGreaterThan(0);
  });

  it('mounts dashboard, album, shared-albums, and account routes as children of the shell', () => {
    expect(findChild('dashboard').component).toBe(DashboardComponent);
    expect(findChild('albums/:id').component).toBe(AlbumDetailComponent);
    expect(findChild('shared-albums').component).toBe(SharedAlbumsComponent);
    expect(findChild('account').component).toBe(AccountSettingsComponent);
  });

  it('protects album-create and album-edit child routes with albumCreatorGuard', () => {
    expect(findChild('albums/create').component).toBe(AlbumsCreateComponent);
    expect(findChild('albums/create').canActivate).toContain(albumCreatorGuard);
    expect(findChild('albums/:id/edit').component).toBe(AlbumEditComponent);
    expect(findChild('albums/:id/edit').canActivate).toContain(albumCreatorGuard);
  });

  it('mounts /admin/settings as an adminGuard-protected child of the shell', () => {
    const adminSettings = findChild('admin/settings');
    expect(adminSettings.component).toBe(AdminSettingsComponent);
    expect(adminSettings.canActivate).toContain(adminGuard);
  });


  it('redirects empty child path to dashboard', () => {
    const empty = findChild('');
    expect(empty.redirectTo).toBe('dashboard');
    expect(empty.pathMatch).toBe('full');
  });

  it('catch-all route redirects to /dashboard (does not clobber the shell)', () => {
    const wildcard = routes.find(r => r.path === '**');
    expect(wildcard).toBeTruthy();
    expect(wildcard!.redirectTo).toBe('/dashboard');
    expect(wildcard!.component).toBeUndefined();
  });
});
