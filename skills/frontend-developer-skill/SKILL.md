---
name: frontend-developer
description: |
  Expert frontend developer guide for PhotoGallery Angular 19.2 implementation with CoreUI components. Use this skill whenever building UI pages, creating Angular components, implementing forms, or connecting to backend APIs. This skill orchestrates frontend development using architect, clean-architecture, coreui-expert, and auth skills for validation. Covers all aspects of frontend development: component design following CoreUI patterns, reactive forms with validation, HTTP service integration with JWT token handling, responsive layouts, accessibility, and E2E testing integration. Includes step-by-step walkthroughs for PhotoGallery screens: login page, admin dashboard, album management, photo upload, access code generation, and photo gallery viewer.
  
  **Dispatch this agent for:**
  - Phase 7: Frontend architecture and CoreUI setup
  - Phase 8: Angular components and pages
  - UI feature implementation for any phase
  
  **Related skills this uses:**
  - **photogallery-architect-skill** - Validates component/service structure for SOLID principles
  - **clean-architecture-guide** - Ensures separation of concerns (services, components, guards)
  - **coreui-expert-skill** - Ensures UI follows CoreUI component patterns and responsive design
  - **photogallery-auth-skill** - Implements JWT token handling and auth guards
  - **playwright-testing-skill** - Works with QA agent for E2E test validation
  
  This skill delegates to copilot-dev-team plugin meta-skills for procedural detail: `coreui-component-recipe` (CoreUI Pro component scaffolding), `angular-service-recipe` (services / inject() / Signals / RxJS), `angular-tdd-jasmine` (Karma + Jasmine specs), `runtime-env-config` (env-driven config baked at container start), and `app-jwt-claims` (JWT claim shape). Auto-trigger these when their conditions match.
---

# Frontend Developer Skill: PhotoGallery Angular Implementation

## Plugin Meta-Skills

The `copilot-dev-team` plugin provides procedural meta-skills that this skill delegates to. They auto-trigger by description match. If there is a conflict, prefer the meta-skill (it is canonical).

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Adding/modifying a CoreUI component | `coreui-component-recipe` | — |
| Adding any `*Service` (HTTP client, state, auth) | `angular-service-recipe` | — |
| Writing Karma + Jasmine specs | `angular-tdd-jasmine` | — |
| Wiring runtime env / API base URL / feature flags | `runtime-env-config` | — |
| Reading/parsing JWT in the FE (roles/claims) | — | `app-jwt-claims` |
| SOLID/DRY validation | `solid-dry-principles` | — |

**Workflow callouts:**

- *→ Component creation / CoreUI usage steps — consult `coreui-component-recipe`.*
- *→ Service creation steps (any `*Service` class) — consult `angular-service-recipe`.*
- *→ Test-writing steps — consult `angular-tdd-jasmine`.*
- *→ Environment / config / API base URL steps — consult `runtime-env-config`.*
- *→ JWT interceptor / auth guard steps — consult `app-jwt-claims` and `identity-and-jwt`.*

## Your Role

You are the frontend developer building PhotoGallery's user interface using Angular 19.2 and CoreUI components. Your responsibilities:

1. **Component Design** - Create reusable Angular components using CoreUI patterns
2. **Forms & Validation** - Build reactive forms with proper validation and error handling
3. **API Integration** - Connect components to backend APIs with proper error handling
4. **JWT Authentication** - Implement HTTP interceptor for JWT token attachment
5. **Responsive Design** - Ensure all pages work across mobile, tablet, desktop
6. **Accessibility** - Follow WCAG 2.1 AA standards with semantic HTML
7. **Compliance** - Reference architect skill for component structure validation
8. **Testing** - Work with QA agent for E2E test coverage

**Before writing any code**, read the related skills:
- **coreui-expert-skill** - Master CoreUI components and responsive design
- **clean-architecture-guide** - Understand service layer, guards, interceptors
- **photogallery-architect-skill** - Know component structure and dependency injection
- **photogallery-auth-skill** - Understand JWT token flow and role-based access

## Phase 7: Frontend Architecture Setup

### Step 1: Install CoreUI Packages

```bash
npm install @coreui/angular-pro @coreui/icons-pro @coreui/coreui-pro --save
npm install bootstrap bootstrap-icons --save
```

### Step 2: Configure CoreUI in app.config.ts

```typescript
import { provideCoreui } from '@coreui/angular-pro';
import { bootstrapCreateTheme } from '@coreui/angular-pro';

export const appConfig: ApplicationConfig = {
  providers: [
    // ... other providers
    provideCoreui({
      theme: bootstrapCreateTheme({
        light: {
          primary: '#0d6efd',
          secondary: '#6c757d',
          danger: '#dc3545'
        }
      })
    })
  ]
};
```

### Step 3: Create Dashboard Layout

```typescript
// src/app/layouts/admin-layout/admin-layout.component.ts
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { SidebarComponent, SidebarModule } from '@coreui/angular-pro';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, SidebarModule],
  template: `
    <div class="wrapper d-flex flex-column min-vh-100">
      <!-- Header -->
      <header class="header sticky-top mb-4 p-0 bg-body-tertiary">
        <div class="container-lg px-4 d-flex">
          <h1 class="my-0">PhotoGallery Admin</h1>
          <div class="ms-auto d-flex align-items-center">
            <button class="btn btn-link" (click)="logout()">Logout</button>
          </div>
        </div>
      </header>
      
      <!-- Main Content -->
      <div class="wrapper d-flex flex-grow-1">
        <!-- Sidebar -->
        <aside class="sidebar bg-dark" style="width: 250px;">
          <nav class="sidebar-nav">
            <a routerLink="/admin/dashboard" routerLinkActive="active" class="nav-link">
              Dashboard
            </a>
            <a routerLink="/admin/albums" routerLinkActive="active" class="nav-link">
              Albums
            </a>
          </nav>
        </aside>
        
        <!-- Page Content -->
        <main class="flex-grow-1">
          <router-outlet></router-outlet>
        </main>
      </div>
    </div>
  `,
  styles: [`
    .sidebar {
      min-width: 250px;
      max-width: 250px;
    }
    .nav-link {
      padding: 0.75rem 1rem;
      display: block;
      color: #fff;
      text-decoration: none;
    }
    .nav-link:hover,
    .nav-link.active {
      background-color: rgba(255, 255, 255, 0.1);
      color: #fff;
    }
  `]
})
export class AdminLayoutComponent {
  constructor(private authService: AuthService, private router: Router) {}
  
  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
```

### Step 4: Setup HTTP Interceptor for JWT

```typescript
// src/app/services/http-token.interceptor.ts
import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';

@Injectable()
export class HttpTokenInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService) {}
  
  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = this.authService.getToken();
    
    if (token) {
      request = request.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }
    
    return next.handle(request);
  }
}
```

### Step 5: Create Auth Guards

```typescript
// src/app/guards/auth.guard.ts
import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}
  
  canActivate(): boolean {
    if (this.authService.isAuthenticated()) {
      return true;
    }
    this.router.navigate(['/login']);
    return false;
  }
}

// src/app/guards/admin.guard.ts
@Injectable({
  providedIn: 'root'
})
export class AdminGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}
  
  canActivate(): boolean {
    if (this.authService.hasRole('Admin')) {
      return true;
    }
    this.router.navigate(['/unauthorized']);
    return false;
  }
}
```

## Phase 8: Angular Components

### Login Page Component

```typescript
// src/app/pages/login/login.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ButtonModule, CardModule } from '@coreui/angular-pro';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, CardModule, ButtonModule],
  template: `
    <div class="d-flex align-items-center justify-content-center min-vh-100 bg-body-tertiary">
      <c-card class="mx-4" style="width: 400px">
        <c-card-body class="p-4">
          <h1 class="text-center mb-3">PhotoGallery</h1>
          <p class="text-body-secondary text-center mb-4">Sign in to your account</p>
          
          <button class="btn btn-primary w-100 mb-3" (click)="loginWithGoogle()">
            Sign in with Google
          </button>
          
          <hr>
          
          <p class="text-body-secondary text-center text-sm">
            Or login as test user (dev only)
          </p>
          <button class="btn btn-secondary w-100" (click)="loginAsTestUser()">
            Test Admin Login
          </button>
        </c-card-body>
      </c-card>
    </div>
  `,
  styles: [`
    c-card {
      border-radius: 0.5rem;
      box-shadow: 0 0.5rem 1rem rgba(0, 0, 0, 0.15);
    }
  `]
})
export class LoginComponent implements OnInit {
  constructor(private authService: AuthService, private router: Router) {}
  
  ngOnInit(): void {
    // Check if we have a token in URL (from OAuth callback)
    const token = new URLSearchParams(window.location.search).get('token');
    if (token) {
      this.authService.setToken(token);
      this.router.navigate(['/admin/dashboard']);
    }
  }
  
  loginWithGoogle(): void {
    window.location.href = `${this.authService.backendUrl}/auth/login`;
  }
  
  loginAsTestUser(): void {
    this.authService.loginAsTestUser().subscribe(() => {
      this.router.navigate(['/admin/dashboard']);
    });
  }
}
```

### Albums List Component

```typescript
// src/app/pages/albums/albums.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AlbumService } from '../../services/album.service';
import { CardModule, ButtonModule, TableModule, AlertModule } from '@coreui/angular-pro';

interface Album {
  id: string;
  title: string;
  description: string;
  createdDate: Date;
  photoCount?: number;
}

@Component({
  selector: 'app-albums',
  standalone: true,
  imports: [CommonModule, RouterModule, CardModule, ButtonModule, TableModule, AlertModule],
  template: `
    <div class="container-lg p-4">
      <div class="d-flex justify-content-between align-items-center mb-4">
        <h1>My Albums</h1>
        <button class="btn btn-primary" routerLink="/admin/albums/create">
          Create Album
        </button>
      </div>
      
      <c-card>
        <c-card-body>
          <div *ngIf="albums.length === 0" cAlert color="info" dismissible>
            No albums yet. <a routerLink="/admin/albums/create">Create one now</a>
          </div>
          
          <div *ngIf="albums.length > 0" class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Description</th>
                  <th>Photos</th>
                  <th>Created</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let album of albums">
                  <td>{{ album.title }}</td>
                  <td>{{ album.description | slice:0:50 }}...</td>
                  <td>{{ album.photoCount || 0 }}</td>
                  <td>{{ album.createdDate | date:'short' }}</td>
                  <td>
                    <button class="btn btn-sm btn-info me-2"
                            [routerLink]="['/admin/albums', album.id]">
                      View
                    </button>
                    <button class="btn btn-sm btn-danger"
                            (click)="deleteAlbum(album.id)">
                      Delete
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </c-card-body>
      </c-card>
    </div>
  `
})
export class AlbumsComponent implements OnInit {
  albums: Album[] = [];
  loading = false;
  error: string | null = null;
  
  constructor(private albumService: AlbumService) {}
  
  ngOnInit(): void {
    this.loadAlbums();
  }
  
  loadAlbums(): void {
    this.loading = true;
    this.albumService.getUserAlbums().subscribe(
      albums => {
        this.albums = albums;
        this.loading = false;
      },
      error => {
        this.error = 'Failed to load albums';
        this.loading = false;
      }
    );
  }
  
  deleteAlbum(albumId: string): void {
    if (confirm('Are you sure you want to delete this album?')) {
      this.albumService.deleteAlbum(albumId).subscribe(
        () => this.loadAlbums(),
        error => this.error = 'Failed to delete album'
      );
    }
  }
}
```

### Album Create/Edit Component

```typescript
// src/app/pages/album-form/album-form.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AlbumService } from '../../services/album.service';
import { CardModule, ButtonModule, FormModule, GridModule } from '@coreui/angular-pro';

@Component({
  selector: 'app-album-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, CardModule, ButtonModule, FormModule, GridModule],
  template: `
    <div class="container-lg p-4">
      <h1 class="mb-4">{{ isEdit ? 'Edit Album' : 'Create Album' }}</h1>
      
      <c-card>
        <c-card-body>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <div cRow class="mb-3">
              <label class="col-md-2 col-form-label">Title</label>
              <div class="col-md-10">
                <input type="text" class="form-control" formControlName="title"
                       [class.is-invalid]="isFieldInvalid('title')" />
                <div class="invalid-feedback" *ngIf="isFieldInvalid('title')">
                  Title is required
                </div>
              </div>
            </div>
            
            <div cRow class="mb-3">
              <label class="col-md-2 col-form-label">Description</label>
              <div class="col-md-10">
                <textarea class="form-control" formControlName="description" rows="4"></textarea>
              </div>
            </div>
            
            <div cRow>
              <div class="col-md-10 offset-md-2">
                <button type="submit" class="btn btn-primary me-2" [disabled]="!form.valid">
                  {{ isEdit ? 'Update' : 'Create' }}
                </button>
                <button type="button" class="btn btn-secondary" (click)="goBack()">
                  Cancel
                </button>
              </div>
            </div>
          </form>
        </c-card-body>
      </c-card>
    </div>
  `
})
export class AlbumFormComponent implements OnInit {
  form: FormGroup;
  isEdit = false;
  loading = false;
  albumId: string | null = null;
  
  constructor(
    private formBuilder: FormBuilder,
    private albumService: AlbumService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.form = this.formBuilder.group({
      title: ['', Validators.required],
      description: ['']
    });
  }
  
  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.albumId = params.get('id');
      if (this.albumId) {
        this.isEdit = true;
        this.loadAlbum(this.albumId);
      }
    });
  }
  
  loadAlbum(albumId: string): void {
    this.albumService.getAlbum(albumId).subscribe(album => {
      this.form.patchValue({
        title: album.title,
        description: album.description
      });
    });
  }
  
  onSubmit(): void {
    if (!this.form.valid) return;
    
    this.loading = true;
    const data = this.form.value;
    
    const request = this.isEdit && this.albumId
      ? this.albumService.updateAlbum(this.albumId, data)
      : this.albumService.createAlbum(data);
    
    request.subscribe(
      () => {
        this.router.navigate(['/admin/albums']);
      },
      error => {
        console.error('Error saving album', error);
        this.loading = false;
      }
    );
  }
  
  isFieldInvalid(fieldName: string): boolean {
    const field = this.form.get(fieldName);
    return !!(field && field.invalid && (field.dirty || field.touched));
  }
  
  goBack(): void {
    this.router.navigate(['/admin/albums']);
  }
}
```

### Photo Upload Component

```typescript
// src/app/pages/photo-upload/photo-upload.component.ts
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { PhotoService } from '../../services/photo.service';
import { CardModule, ButtonModule, ProgressModule } from '@coreui/angular-pro';

@Component({
  selector: 'app-photo-upload',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule, ProgressModule],
  template: `
    <div class="container-lg p-4">
      <h1 class="mb-4">Upload Photos</h1>
      
      <c-card>
        <c-card-body>
          <div class="upload-area border-2 border-dashed p-4 text-center"
               (dragover)="onDragOver($event)"
               (dragleave)="onDragLeave($event)"
               (drop)="onDrop($event)"
               [class.drag-active]="isDragActive">
            <p class="mb-3">
              Drag and drop photos here or 
              <label class="link-primary cursor-pointer">
                select from computer
                <input type="file" #fileInput multiple accept="image/*"
                       (change)="onFileSelected($event)" style="display: none">
              </label>
            </p>
            <small class="text-body-secondary">
              Supported formats: JPG, PNG, WEBP (Max 100MB per file)
            </small>
          </div>
          
          <div *ngIf="uploadProgress > 0" class="mt-4">
            <c-progress>
              <c-progress-bar [value]="uploadProgress" role="progressbar">
                {{ uploadProgress }}%
              </c-progress-bar>
            </c-progress>
          </div>
          
          <div *ngIf="uploadedPhotos.length > 0" class="mt-4">
            <h5>Uploaded Photos</h5>
            <ul class="list-group">
              <li class="list-group-item" *ngFor="let photo of uploadedPhotos">
                {{ photo.fileName }}
              </li>
            </ul>
          </div>
        </c-card-body>
      </c-card>
    </div>
  `,
  styles: [`
    .upload-area {
      border-color: #dee2e6;
      border-radius: 0.25rem;
      cursor: pointer;
      transition: all 0.3s ease;
    }
    .upload-area.drag-active {
      border-color: #0d6efd;
      background-color: rgba(13, 110, 253, 0.1);
    }
    .cursor-pointer {
      cursor: pointer;
    }
  `]
})
export class PhotoUploadComponent {
  isDragActive = false;
  uploadProgress = 0;
  uploadedPhotos: any[] = [];
  albumId: string | null = null;
  
  constructor(
    private photoService: PhotoService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.albumId = this.route.snapshot.paramMap.get('albumId');
  }
  
  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragActive = true;
  }
  
  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragActive = false;
  }
  
  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragActive = false;
    
    const files = event.dataTransfer?.files;
    if (files) {
      this.uploadFiles(files);
    }
  }
  
  onFileSelected(event: Event): void {
    const files = (event.target as HTMLInputElement).files;
    if (files) {
      this.uploadFiles(files);
    }
  }
  
  private uploadFiles(files: FileList): void {
    if (!this.albumId) return;
    
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }
    
    this.photoService.uploadPhotos(this.albumId, formData).subscribe(
      response => {
        this.uploadedPhotos = response.photos;
        this.uploadProgress = 100;
        setTimeout(() => this.uploadProgress = 0, 2000);
      },
      error => {
        console.error('Upload failed', error);
        this.uploadProgress = 0;
      }
    );
  }
}
```

### Access Code Generator Component

```typescript
// src/app/pages/access-code-form/access-code-form.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccessCodeService } from '../../services/access-code.service';
import { CardModule, ButtonModule, FormModule, GridModule } from '@coreui/angular-pro';

@Component({
  selector: 'app-access-code-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, CardModule, ButtonModule, FormModule, GridModule],
  template: `
    <div class="container-lg p-4">
      <h1 class="mb-4">Create Access Code</h1>
      
      <c-card>
        <c-card-body>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <div cRow class="mb-3">
              <label class="col-md-2 col-form-label">Expiration Type</label>
              <div class="col-md-10">
                <select class="form-select" formControlName="expirationType"
                        (change)="onExpirationTypeChange()">
                  <option value="temporary">Expires After (Default 30 Days)</option>
                  <option value="custom">Custom Date</option>
                  <option value="permanent">Never Expires</option>
                </select>
              </div>
            </div>
            
            <div cRow class="mb-3" *ngIf="form.get('expirationType')?.value === 'temporary'">
              <label class="col-md-2 col-form-label">Days Until Expiration</label>
              <div class="col-md-10">
                <input type="number" class="form-control" formControlName="expirationDays"
                       min="1" max="365" />
              </div>
            </div>
            
            <div cRow class="mb-3" *ngIf="form.get('expirationType')?.value === 'custom'">
              <label class="col-md-2 col-form-label">Expiration Date</label>
              <div class="col-md-10">
                <input type="date" class="form-control" formControlName="expirationDate" />
              </div>
            </div>
            
            <div cRow>
              <div class="col-md-10 offset-md-2">
                <button type="submit" class="btn btn-primary me-2">
                  Create Code
                </button>
                <button type="button" class="btn btn-secondary" (click)="goBack()">
                  Cancel
                </button>
              </div>
            </div>
          </form>
        </c-card-body>
      </c-card>
      
      <div *ngIf="generatedCode" class="mt-4">
        <c-card>
          <c-card-header>
            <c-card-title>Access Code Created</c-card-title>
          </c-card-header>
          <c-card-body>
            <p><strong>Share this code with your clients:</strong></p>
            <div class="alert alert-info">
              <input type="text" class="form-control" [value]="generatedCode.code" readonly />
            </div>
            <button class="btn btn-secondary" (click)="copyCode()">
              Copy Code
            </button>
          </c-card-body>
        </c-card>
      </div>
    </div>
  `
})
export class AccessCodeFormComponent implements OnInit {
  form: FormGroup;
  albumId: string | null = null;
  generatedCode: any | null = null;
  
  constructor(
    private formBuilder: FormBuilder,
    private accessCodeService: AccessCodeService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.form = this.formBuilder.group({
      expirationType: ['temporary'],
      expirationDays: [30, [Validators.required, Validators.min(1), Validators.max(365)]],
      expirationDate: ['']
    });
  }
  
  ngOnInit(): void {
    this.albumId = this.route.snapshot.paramMap.get('albumId');
  }
  
  onExpirationTypeChange(): void {
    const expirationDays = this.form.get('expirationDays');
    const expirationDate = this.form.get('expirationDate');
    
    if (this.form.get('expirationType')?.value === 'temporary') {
      expirationDays?.setValidators([Validators.required, Validators.min(1)]);
      expirationDate?.clearValidators();
    } else if (this.form.get('expirationType')?.value === 'custom') {
      expirationDays?.clearValidators();
      expirationDate?.setValidators([Validators.required]);
    } else {
      expirationDays?.clearValidators();
      expirationDate?.clearValidators();
    }
    
    expirationDays?.updateValueAndValidity();
    expirationDate?.updateValueAndValidity();
  }
  
  onSubmit(): void {
    if (!this.form.valid || !this.albumId) return;
    
    const formValue = this.form.value;
    let request;
    
    if (formValue.expirationType === 'temporary') {
      request = this.accessCodeService.createAccessCode(
        this.albumId,
        formValue.expirationDays
      );
    } else if (formValue.expirationType === 'custom') {
      request = this.accessCodeService.createAccessCodeWithDate(
        this.albumId,
        formValue.expirationDate
      );
    } else {
      request = this.accessCodeService.createPermanentAccessCode(this.albumId);
    }
    
    request.subscribe(
      code => {
        this.generatedCode = code;
      },
      error => console.error('Error creating access code', error)
    );
  }
  
  copyCode(): void {
    navigator.clipboard.writeText(this.generatedCode.code);
  }
  
  goBack(): void {
    this.router.navigate(['/admin/albums', this.albumId]);
  }
}
```

### Photo Gallery Viewer (Visitor)

```typescript
// src/app/pages/photo-gallery/photo-gallery.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { AccessCodeService } from '../../services/access-code.service';
import { PhotoService } from '../../services/photo.service';
import { ModalModule, ButtonModule } from '@coreui/angular-pro';

interface Photo {
  id: string;
  fileName: string;
  thumbnailUrl: string;
}

interface PhotoVersion {
  quality: string;
  url: string;
}

@Component({
  selector: 'app-photo-gallery',
  standalone: true,
  imports: [CommonModule, ModalModule, ButtonModule],
  template: `
    <div class="container-lg p-4">
      <div class="mb-4">
        <h1>{{ albumTitle }}</h1>
        <p class="text-body-secondary">{{ albumDescription }}</p>
      </div>
      
      <div class="row g-3">
        <div class="col-lg-3 col-md-4 col-sm-6" *ngFor="let photo of photos">
          <div class="position-relative overflow-hidden" style="aspect-ratio: 1; cursor: pointer;"
               (click)="openPhotoModal(photo)">
            <img [src]="photo.thumbnailUrl" class="w-100 h-100 object-fit-cover"
                 [alt]="photo.fileName" />
          </div>
        </div>
      </div>
      
      <!-- Photo Modal -->
      <c-modal #photoModal tabindex="-1" [visible]="modalVisible" (visibleChange)="modalVisible = $event">
        <c-modal-header class="border-0">
          <c-modal-title>{{ selectedPhoto?.fileName }}</c-modal-title>
          <button type="button" class="btn-close" aria-label="Close" (click)="modalVisible = false"></button>
        </c-modal-header>
        <c-modal-body>
          <img *ngIf="selectedPhoto" [src]="selectedPhoto.thumbnailUrl" class="w-100" />
          
          <div class="mt-3">
            <label class="form-label">Download Quality:</label>
            <select class="form-select" [(ngModel)]="selectedQuality">
              <option value="high">High Quality (Full Size)</option>
              <option value="medium">Medium Quality</option>
              <option value="low">Low Quality (Preview)</option>
            </select>
          </div>
        </c-modal-body>
        <c-modal-footer class="border-0">
          <button type="button" class="btn btn-secondary" (click)="modalVisible = false">
            Close
          </button>
          <button type="button" class="btn btn-primary" (click)="downloadPhoto()">
            Download
          </button>
        </c-modal-footer>
      </c-modal>
    </div>
  `
})
export class PhotoGalleryComponent implements OnInit {
  accessCode: string | null = null;
  albumTitle = '';
  albumDescription = '';
  photos: Photo[] = [];
  
  modalVisible = false;
  selectedPhoto: Photo | null = null;
  selectedQuality = 'high';
  
  constructor(
    private route: ActivatedRoute,
    private accessCodeService: AccessCodeService,
    private photoService: PhotoService
  ) {}
  
  ngOnInit(): void {
    this.accessCode = this.route.snapshot.paramMap.get('code');
    if (this.accessCode) {
      this.loadAlbumByCode(this.accessCode);
    }
  }
  
  loadAlbumByCode(code: string): void {
    this.accessCodeService.getAlbumByCode(code).subscribe(
      album => {
        this.albumTitle = album.title;
        this.albumDescription = album.description;
        this.loadPhotos(code);
      },
      error => console.error('Error loading album', error)
    );
  }
  
  loadPhotos(code: string): void {
    this.photoService.getPhotosByCode(code).subscribe(
      photos => {
        this.photos = photos;
      },
      error => console.error('Error loading photos', error)
    );
  }
  
  openPhotoModal(photo: Photo): void {
    this.selectedPhoto = photo;
    this.modalVisible = true;
  }
  
  downloadPhoto(): void {
    if (!this.selectedPhoto || !this.accessCode) return;
    
    this.photoService.downloadPhoto(
      this.accessCode,
      this.selectedPhoto.id,
      this.selectedQuality as 'high' | 'medium' | 'low'
    ).subscribe(
      blob => {
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = this.selectedPhoto?.fileName || 'photo.jpg';
        link.click();
        window.URL.revokeObjectURL(url);
        this.modalVisible = false;
      },
      error => console.error('Error downloading photo', error)
    );
  }
}
```

## Angular Services

### Auth Service

```typescript
// src/app/services/auth.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  backendUrl = 'http://localhost:8443';
  private tokenSubject = new BehaviorSubject<string | null>(localStorage.getItem('auth_token'));
  
  constructor(private http: HttpClient) {}
  
  setToken(token: string): void {
    localStorage.setItem('auth_token', token);
    this.tokenSubject.next(token);
  }
  
  getToken(): string | null {
    return localStorage.getItem('auth_token');
  }
  
  isAuthenticated(): boolean {
    return !!this.getToken();
  }
  
  hasRole(role: string): boolean {
    const token = this.getToken();
    if (!token) return false;
    
    const payload = JSON.parse(atob(token.split('.')[1]));
    return payload.role === role || payload.roles?.includes(role);
  }
  
  loginAsTestUser(): Observable<any> {
    return this.http.post(`${this.backendUrl}/auth/test-login`, {}).pipe(
      tap(response => {
        if (response.token) {
          this.setToken(response.token);
        }
      })
    );
  }
  
  logout(): void {
    localStorage.removeItem('auth_token');
    this.tokenSubject.next(null);
  }
}
```

### Album Service

```typescript
// src/app/services/album.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Album {
  id: string;
  title: string;
  description: string;
  createdDate: Date;
  photoCount: number;
}

@Injectable({
  providedIn: 'root'
})
export class AlbumService {
  private apiUrl = 'http://localhost:8443/api/albums';
  
  constructor(private http: HttpClient) {}
  
  getUserAlbums(): Observable<Album[]> {
    return this.http.get<Album[]>(this.apiUrl);
  }
  
  getAlbum(id: string): Observable<Album> {
    return this.http.get<Album>(`${this.apiUrl}/${id}`);
  }
  
  createAlbum(data: any): Observable<Album> {
    return this.http.post<Album>(this.apiUrl, data);
  }
  
  updateAlbum(id: string, data: any): Observable<Album> {
    return this.http.put<Album>(`${this.apiUrl}/${id}`, data);
  }
  
  deleteAlbum(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
```

### Photo Service

```typescript
// src/app/services/photo.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class PhotoService {
  private apiUrl = 'http://localhost:8443/api/photos';
  
  constructor(private http: HttpClient) {}
  
  uploadPhotos(albumId: string, formData: FormData): Observable<any> {
    return this.http.post(`${this.apiUrl}/upload/${albumId}`, formData);
  }
  
  getPhotosByCode(code: string): Observable<any[]> {
    return this.http.get<any[]>(`http://localhost:8443/api/code/${code}/photos`);
  }
  
  downloadPhoto(code: string, photoId: string, quality: 'high' | 'medium' | 'low'): Observable<Blob> {
    return this.http.get(
      `http://localhost:8443/api/code/${code}/photo/${photoId}/download?quality=${quality}`,
      { responseType: 'blob' }
    );
  }
}
```

## Quality Checklist

Before committing, verify:

- [ ] Components follow CoreUI patterns (using c-card, c-button, etc.)
- [ ] Responsive design works on mobile/tablet/desktop
- [ ] Forms use reactive forms with validation
- [ ] HTTP calls use auth interceptor (JWT attached)
- [ ] Error handling with user feedback
- [ ] Accessibility (labels, ARIA, semantic HTML)
- [ ] No hardcoded URLs (use environment config)
- [ ] Angular routing setup correctly
- [ ] Lazy loading for large features
- [ ] Performance optimized (OnPush change detection, trackBy in *ngFor)

## References

- **CoreUI Angular:** https://coreui.io/angular/docs/
- **Angular:** https://angular.io/docs
- **Bootstrap 5:** https://getbootstrap.com/docs/5.0/
- **Related Skills:** clean-architecture-guide, coreui-expert-skill, photogallery-auth-skill, playwright-testing-skill
