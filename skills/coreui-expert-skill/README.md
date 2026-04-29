# CoreUI Angular Pro Expert Skill

A comprehensive guide to using CoreUI Angular Pro components, layouts, and patterns for building PhotoGallery's admin dashboard UI.

## What is CoreUI?

CoreUI is a professional Angular component library with:
- **30+ Ready-to-Use Components** (Buttons, Cards, Forms, Tables, Modals, etc.)
- **Professional Styling** - Bootstrap 5 based, fully customizable
- **Admin Dashboard Templates** - Layout patterns we use
- **2000+ Icons** - CoreUI Icons Pro
- **Accessibility** - WCAG 2.1 AA compliant
- **Responsive Design** - Mobile-first, works everywhere

**PhotoGallery uses:** CoreUI Angular Pro + CoreUI Icons Pro

## Quick Start

### Verify Installation

```bash
npm list @coreui/angular-pro @coreui/icons-pro
```

### Import in Your Component

```typescript
// app.component.ts
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CoreuiModule } from '@coreui/angular-pro';
import { CoreuiIconsModule } from '@coreui/icons-angular';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, CoreuiModule, CoreuiIconsModule],
  template: `
    <div class="container">
      <h1>PhotoGallery</h1>
      <button cButton color="primary">Click me</button>
    </div>
  `
})
export class AppComponent {}
```

## Core Concepts

### Grid System (12 columns, Bootstrap 5)
```html
<div class="container">
  <div class="row">
    <div class="col-md-6">Half width on desktop</div>
    <div class="col-md-6">Half width on desktop</div>
  </div>
</div>
```

### Responsive Breakpoints
| Breakpoint | Width | Class |
|-----------|-------|-------|
| Mobile | <576px | (no suffix) |
| Tablet | ≥768px | `-md-` |
| Desktop | ≥992px | `-lg-` |
| Large | ≥1200px | `-xl-` |

### Key Components
1. **Buttons** - CTA and actions
2. **Cards** - Content containers
3. **Forms** - Input and validation
4. **Tables** - Data display
5. **Modals** - Dialogs
6. **Alerts** - Messages
7. **Spinners** - Loading states

## Common Patterns

### Album List with Actions
```html
<table cTable hover striped>
  <thead cTableHead>
    <tr>
      <th>Name</th>
      <th>Photos</th>
      <th>Actions</th>
    </tr>
  </thead>
  <tbody>
    <tr *ngFor="let album of albums">
      <td>{{ album.name }}</td>
      <td>{{ album.photoCount }}</td>
      <td>
        <button cButton color="info" size="sm">Edit</button>
        <button cButton color="danger" size="sm">Delete</button>
      </td>
    </tr>
  </tbody>
</table>
```

### Create Album Modal
```html
<button cButton color="primary" (click)="showModal = true">
  <svg cIcon name="cilPlus"></svg> New Album
</button>

<c-modal [visible]="showModal" (visibleChange)="showModal = $event">
  <div cModalHeader>
    <h5 cModalTitle>Create Album</h5>
  </div>
  <div cModalBody>
    <form [formGroup]="form">
      <div cFormGroup>
        <label cFormLabel>Title</label>
        <input cFormControl type="text" formControlName="title">
      </div>
    </form>
  </div>
  <div cModalFooter>
    <button cButton color="secondary" (click)="showModal = false">Cancel</button>
    <button cButton color="primary" (click)="saveAlbum()">Create</button>
  </div>
</c-modal>
```

### Photo Grid Gallery
```html
<div class="row g-3">
  <div class="col-6 col-md-4" *ngFor="let photo of photos">
    <div cCard>
      <img cCardImage="top" [src]="photo.url" alt="{{ photo.name }}">
      <div cCardBody>
        <p cCardText class="small">{{ photo.name }}</p>
      </div>
    </div>
  </div>
</div>
```

### Dashboard Header
```html
<header cHeader position="sticky" class="mb-3">
  <div class="d-flex justify-content-between align-items-center">
    <h1 cHeaderTitle>Album Management</h1>
    <button cButton color="primary" (click)="logout()">
      <svg cIcon name="cilSignOut"></svg> Logout
    </button>
  </div>
</header>
```

## Related Skills

- **Clean Architecture** - Structure your UI components using layers
- **PhotoGallery Architect** - Validates UI code for consistency
- **Playwright Testing** - Test your CoreUI components with E2E tests

## When to Use This Skill

**Building PhotoGallery UI:**
- Creating album list page
- Building photo gallery
- Designing forms for album creation/editing
- Creating modals for access code generation
- Building upload photo interface
- Designing admin dashboard
- Adding navigation sidebars
- Implementing user profile page

**Styling & Layout:**
- Responsive design for mobile/tablet/desktop
- Spacing and margins (mb-3, p-4, etc.)
- Text and color utilities
- Buttons and icons
- Form validation display

**Component Selection:**
- When to use Card vs Modal
- When to use Table vs Grid
- Choosing button variants and colors
- Icon selection from CoreUI Icons Pro

## Key Files

**Installation location:**
```
node_modules/@coreui/
├── angular-pro/        # Components
├── coreui/             # Styles
├── icons/              # Icon fonts
└── icons-pro/          # Pro icons
```

**Component styles:**
```
node_modules/@coreui/coreui/scss/
```

## Configuration

**Import styles in main.ts or styles.scss:**
```scss
@import '@coreui/coreui/scss/coreui.scss';
@import '@coreui/icons/css/all.min.css';
```

**Module imports (if not standalone):**
```typescript
import { CoreuiModule } from '@coreui/coreui-pro';
import { CoreuiIconsModule } from '@coreui/icons-angular';

@NgModule({
  imports: [CoreuiModule, CoreuiIconsModule]
})
export class AppModule {}
```

## Next Steps

1. Read SKILL.md for complete component reference
2. Explore CoreUI admin dashboard: https://coreui.io/demos/angular/5.5/modern/#/dashboard
3. Use Grid System for responsive layouts
4. Choose appropriate components for PhotoGallery features
5. Reference QUICK_REFERENCE.md for common patterns
6. Check accessibility checklist before submitting UI

---

For detailed component documentation and examples, see **SKILL.md**.
