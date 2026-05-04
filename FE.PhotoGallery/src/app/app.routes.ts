import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { AlbumsCreateComponent } from './components/albums/albums-create.component';
import { AlbumDetailComponent } from './components/albums/album-detail.component';
import { AlbumEditComponent } from './components/albums/album-edit.component';
import { CodeGalleryComponent } from './components/code-gallery/code-gallery.component';
import { authGuard, adminGuard } from './services/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginComponent
  },
  {
    // Public access-code route per requirement #4 — no auth guard.
    // Clients with an access code visit /code/{code} to view an album.
    path: 'code/:code',
    component: CodeGalleryComponent
  },
  {
    path: 'dashboard',
    component: DashboardComponent,
    canActivate: [authGuard]
  },
  {
    path: 'albums/create',
    component: AlbumsCreateComponent,
    canActivate: [authGuard, adminGuard]
  },
  {
    path: 'albums/:id/edit',
    component: AlbumEditComponent,
    canActivate: [authGuard, adminGuard]
  },
  {
    path: 'albums/:id',
    component: AlbumDetailComponent,
    canActivate: [authGuard]
  },
  {
    path: '',
    component: DashboardComponent,
    canActivate: [authGuard]
  },
  {
    path: '**',
    component: DashboardComponent,
    canActivate: [authGuard]
  }
];
