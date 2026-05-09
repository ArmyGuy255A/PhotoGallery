---
name: coreui-angular
description: |
  CoreUI Angular Pro expert for PhotoGallery UI. This skill covers CoreUI Angular Pro components (buttons, cards, modals, forms, tables, alerts, spinners), layout patterns (containers, rows, columns), styling and theming, dashboard design patterns, form validation, and accessibility. Use this whenever building PhotoGallery UI components, creating pages, styling layouts, designing dashboards, building forms, implementing tables or modals, selecting icons from coreui-icons-pro, or ensuring UI consistency. Consult this skill to match CoreUI admin dashboard patterns and maintain consistent styling across the application. Explains how to use CoreUI utilities, responsive design, and component composition.

  This skill delegates to copilot-dev-team plugin meta-skills: `coreui-component-recipe` (canonical CoreUI Pro 5.4 component catalog, theming, Pro-only widgets, forms patterns) and `angular-service-recipe` / `angular-tdd-jasmine` for the surrounding Angular plumbing. Auto-trigger these when their conditions match. The plugin's `coreui-component-recipe` is the canonical reference — prefer it when there's a conflict.
---

# CoreUI Angular Pro Expert Guide for PhotoGallery

## Plugin Meta-Skills

`copilot-dev-team`'s `coreui-component-recipe` ships an authoritative reference catalog for CoreUI Pro 5.4 (catalog, forms, theming, Pro-only widgets) and is auto-triggered by description match. Defer to it for component selection and patterns.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Picking a CoreUI Pro component for a feature | `coreui-component-recipe` | — |
| Theming / SCSS variable customization | `coreui-component-recipe` (theming reference) | — |
| Building forms with CoreUI inputs/validation | `coreui-component-recipe` (forms reference) | — |
| Wrapping CoreUI in an Angular service | — | `angular-service-recipe` |
| Writing specs for a CoreUI-using component | — | `angular-tdd-jasmine` |

**Workflow callouts:**

- *→ Component-pick / catalog sections — consult `coreui-component-recipe` for the canonical catalog and Pro-only widgets.*
- *→ Theming / SCSS sections — consult `coreui-component-recipe` (theming reference).*
- *→ Form-building sections — consult `coreui-component-recipe` (forms reference).*

## What is CoreUI?

CoreUI is a professional UI kit for Angular with pre-built, customizable components following modern design patterns. PhotoGallery uses **CoreUI Angular Pro** which includes:

- 📦 **30+ Components** (Buttons, Cards, Modals, Forms, Tables, etc.)
- 🎨 **Professional Styling** - Bootstrap 5 based, fully customizable
- 🏢 **Admin Dashboard Templates** - Layout patterns for PhotoGallery
- 🔐 **Accessibility** - WCAG 2.1 AA compliant
- 📱 **Responsive Design** - Mobile-first, works on all devices
- 🎭 **Icons** - CoreUI Icons Pro (2000+ icons)
- 🌙 **Dark Mode** - Built-in theme support

**PhotoGallery's UI Goal:** Build admin dashboard for album/photo management, inspired by https://coreui.io/demos/angular/5.5/modern/#/dashboard

## Installation & Setup

### Verify CoreUI Installation

```bash
# In FE.PhotoGallery directory
npm list @coreui/angular-pro

# Should see:
# @coreui/angular-pro@5.x.x
# @coreui/icons-pro@3.x.x
# @coreui/coreui@5.x.x
```

### Module Imports

```typescript
// app.config.ts (standalone setup) or app.module.ts
import { CoreuiModule } from '@coreui/angular-pro';
import { CoreuiIconsModule } from '@coreui/icons-angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(),
    // ... other providers
  ]
};

export const coreui = {
  imports: [
    CoreuiModule,
    CoreuiIconsModule,
  ]
};
```

### Import Styles

```typescript
// main.ts or styles.scss
import '@coreui/coreui/scss/coreui.scss';
import '@coreui/icons/css/all.min.css';
```

## CoreUI Grid System

Bootstrap 5 based, 12-column responsive grid

### Basic Layout

```html
<!-- Container: full-width or fixed -->
<div class="container">
  <div class="row">
    <div class="col-12">Full width</div>
  </div>
</div>

<!-- Container-Fluid: always full-width -->
<div class="container-fluid">
  <div class="row">
    <div class="col-md-6">Half on medium+ screens</div>
    <div class="col-md-6">Half on medium+ screens</div>
  </div>
</div>

<!-- Responsive columns -->
<div class="row">
  <div class="col-12 col-sm-6 col-md-4 col-lg-3">
    Responsive: full width on mobile, 6 on tablet, 4 on desktop, 3 on large
  </div>
</div>

<!-- Equal columns (auto) -->
<div class="row">
  <div class="col">Auto-width column</div>
  <div class="col">Auto-width column</div>
  <div class="col">Auto-width column</div>
</div>
```

### Grid Breakpoints

| Breakpoint | Size | Class |
|-----------|------|-------|
| Extra Small | <576px | (no suffix) |
| Small | ≥576px | `-sm-` |
| Medium | ≥768px | `-md-` |
| Large | ≥992px | `-lg-` |
| Extra Large | ≥1200px | `-xl-` |
| XXL | ≥1400px | `-xxl-` |

## Core Components

### Buttons

*→ consult `coreui-component-recipe` for the canonical button catalog and variants*

```html
<!-- Standard buttons -->
<button cButton color="primary">Primary</button>
<button cButton color="secondary">Secondary</button>
<button cButton color="success">Success</button>
<button cButton color="danger">Danger</button>
<button cButton color="warning">Warning</button>
<button cButton color="info">Info</button>
<button cButton color="light">Light</button>
<button cButton color="dark">Dark</button>

<!-- Button sizes -->
<button cButton color="primary" size="lg">Large</button>
<button cButton color="primary" size="sm">Small</button>

<!-- Button states -->
<button cButton color="primary" [disabled]="true">Disabled</button>
<button cButton color="primary" [active]="true">Active</button>

<!-- Outline variant -->
<button cButton color="primary" variant="outline">Outline</button>

<!-- Loading state with spinner -->
<button cButton color="primary" [disabled]="isLoading">
  <c-spinner *ngIf="isLoading" size="sm" class="me-2"></c-spinner>
  {{ isLoading ? 'Loading...' : 'Save' }}
</button>

<!-- Button group -->
<div cButtonGroup>
  <button cButton color="primary" [active]="view === 'grid'">Grid</button>
  <button cButton color="primary" [active]="view === 'list'">List</button>
</div>
```

### Cards

```html
<!-- Simple card -->
<div cCard>
  <div cCardBody>
    <div cCardTitle>Card Title</div>
    <p cCardText>Card content here</p>
    <button cButton color="primary">Action</button>
  </div>
</div>

<!-- Card with header and footer -->
<div cCard>
  <div cCardHeader>
    <strong>Album Title</strong>
  </div>
  <div cCardBody>
    <p cCardText>Card content</p>
  </div>
  <div cCardFooter>
    <small class="text-muted">Last modified 2 days ago</small>
  </div>
</div>

<!-- Card with image -->
<div cCard>
  <img cCardImage="top" src="album-preview.jpg" alt="Album">
  <div cCardBody>
    <div cCardTitle>Album Name</div>
    <p cCardText>Description</p>
    <a href="#" cButton color="primary" size="sm">View Photos</a>
  </div>
</div>

<!-- Card deck (equal height cards) -->
<div class="row">
  <div class="col-md-4">
    <div cCard>
      <div cCardBody>
        <div cCardTitle>Album 1</div>
        <p cCardText>5 photos</p>
      </div>
    </div>
  </div>
  <div class="col-md-4">
    <div cCard>
      <div cCardBody>
        <div cCardTitle>Album 2</div>
        <p cCardText>12 photos</p>
      </div>
    </div>
  </div>
</div>
```

### Forms & Input

*→ consult `coreui-component-recipe` (forms reference) for CoreUI form inputs, validation patterns, and Pro form widgets*

```html
<!-- Form group (label + input) -->
<form [formGroup]="albumForm">
  <div cFormGroup>
    <label cFormLabel for="title">Album Title</label>
    <input 
      cFormControl 
      type="text" 
      id="title"
      formControlName="title"
      placeholder="Enter album title">
    <div *ngIf="albumForm.get('title')?.errors?.required" 
         class="form-text text-danger">
      Title is required
    </div>
  </div>

  <!-- Text area -->
  <div cFormGroup>
    <label cFormLabel for="description">Description</label>
    <textarea 
      cFormControl 
      id="description"
      rows="4"
      formControlName="description"
      placeholder="Enter album description"></textarea>
  </div>

  <!-- Select dropdown -->
  <div cFormGroup>
    <label cFormLabel for="category">Category</label>
    <select 
      cFormControl 
      id="category"
      formControlName="category">
      <option value="">Select category</option>
      <option value="wedding">Wedding</option>
      <option value="portrait">Portrait</option>
      <option value="landscape">Landscape</option>
    </select>
  </div>

  <!-- Checkbox -->
  <div cFormCheck>
    <input 
      type="checkbox" 
      cFormCheckInput 
      id="published"
      formControlName="published">
    <label cFormCheckLabel for="published">
      Publish immediately
    </label>
  </div>

  <!-- Radio buttons -->
  <div class="mb-3">
    <label class="form-label">Photo Quality</label>
    <div cFormCheck>
      <input 
        type="radio" 
        cFormCheckInput 
        name="quality"
        id="quality-high"
        value="high"
        formControlName="quality">
      <label cFormCheckLabel for="quality-high">High</label>
    </div>
    <div cFormCheck>
      <input 
        type="radio" 
        cFormCheckInput 
        name="quality"
        id="quality-medium"
        value="medium"
        formControlName="quality">
      <label cFormCheckLabel for="quality-medium">Medium</label>
    </div>
  </div>

  <!-- Form submission -->
  <button 
    cButton 
    color="primary" 
    [disabled]="albumForm.invalid || isSubmitting">
    {{ isSubmitting ? 'Creating...' : 'Create Album' }}
  </button>
</form>
```

### Tables

```html
<!-- Basic table -->
<table cTable>
  <thead cTableHead>
    <tr>
      <th>Album Name</th>
      <th>Photos</th>
      <th>Created</th>
      <th>Action</th>
    </tr>
  </thead>
  <tbody>
    <tr *ngFor="let album of albums">
      <td><strong>{{ album.name }}</strong></td>
      <td>{{ album.photoCount }}</td>
      <td>{{ album.createdDate | date: 'short' }}</td>
      <td>
        <button cButton color="info" size="sm" (click)="editAlbum(album.id)">
          Edit
        </button>
        <button cButton color="danger" size="sm" (click)="deleteAlbum(album.id)">
          Delete
        </button>
      </td>
    </tr>
  </tbody>
</table>

<!-- Hover effect -->
<table cTable hover>
  <!-- rows highlight on hover -->
</table>

<!-- Striped rows -->
<table cTable striped>
  <!-- alternate row colors -->
</table>

<!-- Bordered table -->
<table cTable bordered>
  <!-- all borders visible -->
</table>

<!-- Responsive table (scrollable on small screens) -->
<div cTableResponsive>
  <table cTable>
    <!-- content -->
  </table>
</div>

<!-- Table with status indicators -->
<table cTable hover>
  <thead cTableHead>
    <tr>
      <th>Name</th>
      <th>Status</th>
      <th>Last Modified</th>
    </tr>
  </thead>
  <tbody>
    <tr *ngFor="let album of albums">
      <td>{{ album.name }}</td>
      <td>
        <c-badge 
          color="{{ album.published ? 'success' : 'warning' }}">
          {{ album.published ? 'Published' : 'Draft' }}
        </c-badge>
      </td>
      <td>{{ album.updatedDate | timeago }}</td>
    </tr>
  </tbody>
</table>
```

### Modals

```html
<!-- Modal trigger button -->
<button cButton color="primary" (click)="showModal = true">
  Create Album
</button>

<!-- Modal component -->
<c-modal 
  [visible]="showModal" 
  (visibleChange)="showModal = $event"
  id="albumModal">
  <div cModalHeader>
    <h5 cModalTitle>Create New Album</h5>
  </div>
  <div cModalBody>
    <form [formGroup]="albumForm">
      <div cFormGroup>
        <label cFormLabel>Album Title</label>
        <input 
          cFormControl 
          type="text"
          formControlName="title">
      </div>
      <div cFormGroup>
        <label cFormLabel>Description</label>
        <textarea 
          cFormControl 
          formControlName="description"></textarea>
      </div>
    </form>
  </div>
  <div cModalFooter>
    <button 
      cButton 
      color="secondary"
      (click)="showModal = false">
      Cancel
    </button>
    <button 
      cButton 
      color="primary"
      (click)="saveAlbum()">
      Create
    </button>
  </div>
</c-modal>
```

### Alerts & Notifications

```html
<!-- Success alert -->
<c-alert color="success" [dismissible]="true">
  <strong>Success!</strong> Album created successfully.
</c-alert>

<!-- Error alert -->
<c-alert color="danger">
  <strong>Error!</strong> Failed to upload photos.
</c-alert>

<!-- Warning alert -->
<c-alert color="warning">
  <strong>Warning!</strong> This album has no photos yet.
</c-alert>

<!-- Info alert -->
<c-alert color="info" (close)="dismissedAlert = true">
  <strong>Info:</strong> You can share this album with access codes.
</c-alert>

<!-- Dismissible alert with close button -->
<c-alert 
  color="success" 
  [dismissible]="true"
  (close)="onAlertClosed()">
  <strong>Success!</strong> Your changes have been saved.
</c-alert>
```

### Spinners & Loading

```html
<!-- Spinner (loading indicator) -->
<c-spinner></c-spinner>

<!-- Colored spinner -->
<c-spinner color="primary"></c-spinner>

<!-- Small spinner (for buttons) -->
<c-spinner size="sm"></c-spinner>

<!-- With text -->
<div class="text-center">
  <c-spinner></c-spinner>
  <p class="mt-3">Loading photos...</p>
</div>

<!-- Border spinner -->
<c-spinner type="border"></c-spinner>

<!-- Grow spinner -->
<c-spinner type="grow"></c-spinner>
```

### Breadcrumbs

```html
<!-- Breadcrumb navigation -->
<c-breadcrumb>
  <c-breadcrumb-item>
    <a href="/" routerLink="/">Home</a>
  </c-breadcrumb-item>
  <c-breadcrumb-item>
    <a href="/albums" routerLink="/albums">Albums</a>
  </c-breadcrumb-item>
  <c-breadcrumb-item active>
    {{ currentAlbum.name }}
  </c-breadcrumb-item>
</c-breadcrumb>
```

### Badges

```html
<!-- Badge variants -->
<span c-badge color="primary">Primary</span>
<span c-badge color="success">Published</span>
<span c-badge color="warning">Draft</span>
<span c-badge color="danger">Archived</span>

<!-- Badge with count -->
<span c-badge color="primary" shape="rounded-pill">
  {{ notificationCount }}
</span>

<!-- Badge in table -->
<td>
  <c-badge color="{{ photo.quality === 'high' ? 'success' : 'info' }}">
    {{ photo.quality }}
  </c-badge>
</td>
```

## Icons (CoreUI Icons Pro)

CoreUI Icons Pro includes 2000+ professional icons

```html
<!-- Icon in button -->
<button cButton color="primary">
  <svg cIcon name="cilPlus" size="lg"></svg>
  Add Album
</button>

<!-- Icon standalone -->
<svg cIcon name="cilCheckAlt" class="text-success"></svg>

<!-- Colored icon -->
<svg cIcon name="cilWarning" class="text-warning" size="xl"></svg>

<!-- Common icons for PhotoGallery -->
<svg cIcon name="cilPlus"></svg>          <!-- Add -->
<svg cIcon name="cilPencil"></svg>        <!-- Edit -->
<svg cIcon name="cilTrash"></svg>         <!-- Delete -->
<svg cIcon name="cilCheckAlt"></svg>      <!-- Success/Check -->
<svg cIcon name="cilX"></svg>             <!-- Close/Cancel -->
<svg cIcon name="cilSearch"></svg>        <!-- Search -->
<svg cIcon name="cilImage"></svg>         <!-- Photo/Image -->
<svg cIcon name="cilStar"></svg>          <!-- Favorite -->
<svg cIcon name="cilDown"></svg>          <!-- Download -->
<svg cIcon name="cilShare"></svg>         <!-- Share -->
<svg cIcon name="cilSettings"></svg>      <!-- Settings -->
<svg cIcon name="cilMenu"></svg>          <!-- Menu/Hamburger -->
<svg cIcon name="cilUser"></svg>          <!-- User/Profile -->
<svg cIcon name="cilSignOut"></svg>       <!-- Logout -->
<svg cIcon name="cilLockLocked"></svg>    <!-- Lock -->
<svg cIcon name="cilCloudUpload"></svg>   <!-- Upload -->

<!-- Icon sizes -->
<svg cIcon name="cilPlus" size="sm"></svg>
<svg cIcon name="cilPlus" size="lg"></svg>
<svg cIcon name="cilPlus" size="xl"></svg>
<svg cIcon name="cilPlus" size="2xl"></svg>
```

## Dashboard Layout Pattern

PhotoGallery should use CoreUI's dashboard layout:

```typescript
// app.component.ts
import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  template: `
    <div class="wrapper d-flex flex-column min-vh-100">
      <!-- Sidebar -->
      <app-sidebar></app-sidebar>

      <!-- Header -->
      <app-header></app-header>

      <!-- Main content -->
      <div class="body flex-grow-1">
        <div class="container-lg px-4">
          <router-outlet></router-outlet>
        </div>
      </div>

      <!-- Footer -->
      <app-footer></app-footer>
    </div>
  `
})
export class AppComponent {}
```

### Sidebar Navigation

```html
<!-- sidebar.component.html -->
<div cSidebar placement="start" id="sidebar" visible="true" (clickOutside)="visible = false">
  <div cSidebarHeader class="mb-3">
    <strong>PhotoGallery</strong>
  </div>
  
  <c-sidebar-nav>
    <!-- Admin menu items -->
    <c-sidebar-nav-item 
      *ngIf="isAdmin"
      href="#"
      routerLink="/albums"
      [routerLinkActive]="'active'">
      <svg cIcon name="cilImage" cSidebarNavIcon></svg>
      Albums
    </c-sidebar-nav-item>

    <c-sidebar-nav-item 
      *ngIf="isAdmin"
      href="#"
      routerLink="/upload"
      [routerLinkActive]="'active'">
      <svg cIcon name="cilCloudUpload" cSidebarNavIcon></svg>
      Upload Photos
    </c-sidebar-nav-item>

    <c-sidebar-nav-divider></c-sidebar-nav-divider>

    <c-sidebar-nav-item 
      href="#"
      routerLink="/profile"
      [routerLinkActive]="'active'">
      <svg cIcon name="cilUser" cSidebarNavIcon></svg>
      Profile
    </c-sidebar-nav-item>

    <c-sidebar-nav-item 
      href="#"
      (click)="logout()">
      <svg cIcon name="cilSignOut" cSidebarNavIcon></svg>
      Logout
    </c-sidebar-nav-item>
  </c-sidebar-nav>
</div>
```

### Header with User Menu

```html
<!-- header.component.html -->
<header cHeader position="sticky" class="p-0 mb-3">
  <div cContainer breakpoint="lg" class="px-4" fluid>
    <button 
      cHeaderToggler
      cSidebarToggle
      size="lg"
      type="button"
      class="d-md-none me-3">
      <svg cIcon name="cilMenu" size="lg"></svg>
    </button>

    <div class="d-flex justify-content-between w-100">
      <div>
        <h1 cHeaderTitle>Album Management</h1>
      </div>

      <div cHeaderNav>
        <!-- User dropdown -->
        <div cHeaderNavItem>
          <a href="#" class="c-header-nav-link" 
             [cDropdown]="true"
             [cDropdownToggle]="true"
             container="body">
            <img 
              alt="User Avatar" 
              src="user-avatar.jpg"
              class="c-avatar-img">
          </a>
          <div cDropdownMenu 
               class="pt-0" 
               placement="bottom-end">
            <a cDropdownItem href="#">Profile</a>
            <a cDropdownItem href="#">Settings</a>
            <div cDropdownDivider></div>
            <a cDropdownItem href="#" (click)="logout()">Logout</a>
          </div>
        </div>
      </div>
    </div>
  </div>
</header>
```

## Styling & Utilities

*→ consult `coreui-component-recipe` (theming reference) for SCSS customization, CSS variables, and theme overrides*

### Spacing (Margin & Padding)

```html
<!-- Margin: m-{property}-{size} -->
<div class="mb-3">margin-bottom: 1rem</div>
<div class="mt-2">margin-top: 0.5rem</div>
<div class="mx-auto">margin-left/right: auto (center)</div>

<!-- Padding: p-{property}-{size} -->
<div class="p-3">padding: 1rem</div>
<div class="px-4">padding-left/right: 1.5rem</div>

<!-- Sizes: 0, 1 (0.25rem), 2 (0.5rem), 3 (1rem), 4 (1.5rem), 5 (3rem) -->
<div class="m-5">large margin</div>
```

### Text Utilities

```html
<!-- Text alignment -->
<p class="text-start">Left aligned</p>
<p class="text-center">Center aligned</p>
<p class="text-end">Right aligned</p>

<!-- Text color -->
<p class="text-primary">Primary text</p>
<p class="text-success">Success text</p>
<p class="text-danger">Danger text</p>
<p class="text-muted">Muted text</p>

<!-- Text size -->
<p class="fs-1">Large text</p>
<p class="fs-6">Small text</p>

<!-- Text weight -->
<p class="fw-bold">Bold</p>
<p class="fw-normal">Normal</p>
<p class="fw-light">Light</p>

<!-- Text decoration -->
<p class="text-decoration-underline">Underlined</p>
<p class="text-decoration-line-through">Strikethrough</p>
```

### Display & Flexbox

```html
<!-- Display -->
<div class="d-block">Block element</div>
<div class="d-inline">Inline element</div>
<div class="d-flex">Flex container</div>
<div class="d-none">Hidden</div>
<div class="d-md-none">Hidden on medium+ screens</div>

<!-- Flexbox alignment -->
<div class="d-flex justify-content-between">
  <span>Left</span>
  <span>Right</span>
</div>

<div class="d-flex align-items-center">
  <!-- Items vertically centered -->
</div>

<div class="d-flex flex-column">
  <!-- Stack vertically -->
</div>

<!-- Gap between items -->
<div class="d-flex gap-3">
  <div>Item 1</div>
  <div>Item 2</div>
</div>
```

### Borders & Shadows

```html
<!-- Border -->
<div class="border">All borders</div>
<div class="border-top">Top border</div>
<div class="border-danger">Danger color border</div>

<!-- Rounded corners -->
<div class="rounded">Rounded corners</div>
<div class="rounded-5">Very rounded</div>
<div class="rounded-circle">Circle</div>

<!-- Shadows -->
<div class="shadow">Subtle shadow</div>
<div class="shadow-lg">Large shadow</div>
```

## Responsive Design Best Practices

```html
<!-- Hidden on small screens, visible on medium+ -->
<div class="d-none d-md-block">
  Desktop content
</div>

<!-- Visible on small, hidden on medium+ -->
<div class="d-md-none">
  Mobile navigation
</div>

<!-- Responsive padding -->
<div class="p-2 p-md-4">
  Small padding on mobile, larger on desktop
</div>

<!-- Responsive grid -->
<div class="row">
  <div class="col-12 col-md-6 col-lg-3">
    Full width on mobile, half on tablet, quarter on desktop
  </div>
</div>
```

## Form Validation Patterns

```typescript
// form.component.ts
import { Component } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';

@Component({
  selector: 'app-album-form',
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()">
      <div cFormGroup>
        <label cFormLabel for="title">Album Title</label>
        <input 
          cFormControl
          type="text"
          id="title"
          formControlName="title"
          [class.is-invalid]="isFieldInvalid('title')">
        <div 
          *ngIf="isFieldInvalid('title')" 
          class="invalid-feedback">
          {{ getErrorMessage('title') }}
        </div>
      </div>

      <button 
        cButton 
        color="primary"
        [disabled]="form.invalid">
        Create Album
      </button>
    </form>
  `
})
export class AlbumFormComponent {
  form = this.fb.group({
    title: ['', [Validators.required, Validators.minLength(3)]],
    description: ['', Validators.maxLength(500)],
  });

  constructor(private fb: FormBuilder) {}

  isFieldInvalid(fieldName: string): boolean {
    const field = this.form.get(fieldName);
    return !!(field && field.invalid && (field.dirty || field.touched));
  }

  getErrorMessage(fieldName: string): string {
    const field = this.form.get(fieldName);
    if (field?.errors?.['required']) return `${fieldName} is required`;
    if (field?.errors?.['minlength']) 
      return `${fieldName} must be at least ${field.errors['minlength'].requiredLength} characters`;
    return '';
  }

  onSubmit() {
    if (this.form.valid) {
      // Submit form
    }
  }
}
```

## Common PhotoGallery Components

### Album Card

```typescript
@Component({
  selector: 'app-album-card',
  template: `
    <div cCard class="h-100">
      <img cCardImage="top" [src]="album.previewUrl" alt="{{ album.name }}">
      <div cCardBody>
        <h5 cCardTitle>{{ album.name }}</h5>
        <p cCardText class="text-muted">{{ album.photoCount }} photos</p>
        <div class="d-flex gap-2">
          <button cButton color="primary" size="sm" (click)="viewAlbum()">
            <svg cIcon name="cilEye" size="sm"></svg> View
          </button>
          <button cButton color="info" size="sm" (click)="editAlbum()">
            <svg cIcon name="cilPencil" size="sm"></svg> Edit
          </button>
          <button cButton color="danger" size="sm" (click)="deleteAlbum()">
            <svg cIcon name="cilTrash" size="sm"></svg>
          </button>
        </div>
      </div>
    </div>
  `
})
export class AlbumCardComponent {
  @Input() album!: Album;
  @Output() view = new EventEmitter<Album>();
  @Output() edit = new EventEmitter<Album>();
  @Output() delete = new EventEmitter<Album>();

  viewAlbum() { this.view.emit(this.album); }
  editAlbum() { this.edit.emit(this.album); }
  deleteAlbum() { this.delete.emit(this.album); }
}
```

### Photo Grid

```typescript
@Component({
  selector: 'app-photo-grid',
  template: `
    <div class="row g-3">
      <div class="col-6 col-md-4 col-lg-3" *ngFor="let photo of photos">
        <div class="card h-100 position-relative overflow-hidden cursor-pointer"
             (click)="selectPhoto(photo)"
             [class.selected]="isSelected(photo)">
          <img [src]="photo.thumbUrl" alt="{{ photo.name }}" class="card-img-top">
          <div class="position-absolute top-0 start-0 w-100 h-100 d-flex align-items-center justify-content-center opacity-0 hover-overlay"
               [style.background-color]="'rgba(0, 0, 0, 0.5)'">
            <svg cIcon name="cilZoom" size="2xl" class="text-white"></svg>
          </div>
          <div *ngIf="isSelected(photo)" class="position-absolute top-0 end-0">
            <svg cIcon name="cilCheckAlt" size="lg" class="text-success"></svg>
          </div>
        </div>
      </div>
    </div>
  `
})
export class PhotoGridComponent {
  @Input() photos: Photo[] = [];
  @Input() selectedPhotos: Photo[] = [];
  @Output() photoSelected = new EventEmitter<Photo>();

  selectPhoto(photo: Photo) {
    this.photoSelected.emit(photo);
  }

  isSelected(photo: Photo): boolean {
    return this.selectedPhotos.some(p => p.id === photo.id);
  }
}
```

## Accessibility Checklist

- [x] All form inputs have associated labels
- [x] Color not used as only means of communication (use icons/text too)
- [x] Sufficient color contrast (WCAG AA)
- [x] Buttons have meaningful text or aria-labels
- [x] Images have alt text
- [x] Error messages linked to form fields
- [x] Keyboard navigation works (Tab, Enter, etc.)
- [x] Focus indicators visible
- [x] ARIA attributes used appropriately

---

**Key Takeaway:** CoreUI provides professional, accessible components following Bootstrap 5. Use the grid system for layout, components for structure, utilities for styling, and icons for visual communication. Maintain consistency by using CoreUI components throughout PhotoGallery.

## Cross-cutting plugin skills (always-on)

- `scratch-discipline` — temp files in `.copilot/scratch/<task-id>/`.
- `secret-hygiene` — no secrets in components or env files.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only.
- `copilot-memory-update` — record durable cross-session decisions.
