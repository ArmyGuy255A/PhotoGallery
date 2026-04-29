# CoreUI Angular - Quick Reference

One-page cheat sheet for PhotoGallery UI developers.

## Grid System (Bootstrap 5)

```html
<!-- Full width container -->
<div class="container">
  <div class="row">
    <div class="col-md-6">50% on tablet+</div>
    <div class="col-md-6">50% on tablet+</div>
  </div>
</div>

<!-- Fluid container (always full) -->
<div class="container-fluid">
  <div class="row">
    <div class="col-lg-3">25% on desktop</div>
    <div class="col-lg-3">25% on desktop</div>
    <div class="col-lg-3">25% on desktop</div>
    <div class="col-lg-3">25% on desktop</div>
  </div>
</div>
```

**Breakpoints:** `-sm-` (576px), `-md-` (768px), `-lg-` (992px), `-xl-` (1200px)

## Common Components

| Component | Usage | Attribute |
|-----------|-------|-----------|
| Button | Actions, CTAs | `cButton` |
| Card | Content container | `cCard` |
| Form | User input | `cFormGroup`, `cFormControl` |
| Table | Data display | `cTable` |
| Modal | Dialog/popup | `c-modal` |
| Alert | Messages | `c-alert` |
| Badge | Status indicator | `c-badge` |
| Spinner | Loading | `c-spinner` |

## Quick Code

### Button

```html
<button cButton color="primary">Primary</button>
<button cButton color="success" size="lg">Large</button>
<button cButton color="danger" [disabled]="true">Disabled</button>
<button cButton color="primary" variant="outline">Outline</button>

<!-- With icon -->
<button cButton color="primary">
  <svg cIcon name="cilPlus"></svg> Add
</button>

<!-- Loading state -->
<button cButton [disabled]="loading">
  <c-spinner *ngIf="loading" size="sm" class="me-2"></c-spinner>
  {{ loading ? 'Loading...' : 'Save' }}
</button>
```

### Card

```html
<div cCard>
  <div cCardBody>
    <h5 cCardTitle>Card Title</h5>
    <p cCardText>Content here</p>
    <button cButton color="primary">Action</button>
  </div>
</div>

<!-- With image -->
<div cCard>
  <img cCardImage="top" src="image.jpg" alt="Image">
  <div cCardBody>
    <h5 cCardTitle>Title</h5>
  </div>
</div>
```

### Form

```html
<form [formGroup]="form" (ngSubmit)="save()">
  <div cFormGroup>
    <label cFormLabel>Title</label>
    <input cFormControl type="text" formControlName="title">
  </div>

  <div cFormGroup>
    <label cFormLabel>Description</label>
    <textarea cFormControl formControlName="description"></textarea>
  </div>

  <div cFormCheck>
    <input type="checkbox" cFormCheckInput id="published" formControlName="published">
    <label cFormCheckLabel for="published">Publish</label>
  </div>

  <button cButton color="primary" [disabled]="form.invalid">Save</button>
</form>
```

### Table

```html
<table cTable hover striped>
  <thead cTableHead>
    <tr>
      <th>Name</th>
      <th>Count</th>
      <th>Action</th>
    </tr>
  </thead>
  <tbody>
    <tr *ngFor="let item of items">
      <td>{{ item.name }}</td>
      <td>{{ item.count }}</td>
      <td>
        <button cButton color="info" size="sm">Edit</button>
        <button cButton color="danger" size="sm">Delete</button>
      </td>
    </tr>
  </tbody>
</table>

<!-- Responsive table (scrollable on mobile) -->
<div cTableResponsive>
  <table cTable><!-- content --></table>
</div>
```

### Modal

```html
<button cButton color="primary" (click)="modal = true">Open</button>

<c-modal [visible]="modal" (visibleChange)="modal = $event">
  <div cModalHeader>
    <h5 cModalTitle>Modal Title</h5>
  </div>
  <div cModalBody>
    Modal content
  </div>
  <div cModalFooter>
    <button cButton color="secondary" (click)="modal = false">Cancel</button>
    <button cButton color="primary" (click)="confirm()">Confirm</button>
  </div>
</c-modal>
```

### Alert

```html
<c-alert color="success" [dismissible]="true">
  <strong>Success!</strong> Operation completed.
</c-alert>

<c-alert color="danger">
  <strong>Error:</strong> Something went wrong.
</c-alert>

<c-alert color="warning">Attention needed</c-alert>

<c-alert color="info">Information</c-alert>
```

### Badge & Status

```html
<c-badge color="primary">Primary</c-badge>
<c-badge color="success">Active</c-badge>
<c-badge color="warning">Warning</c-badge>
<c-badge color="danger">Critical</c-badge>

<!-- In table -->
<td>
  <c-badge color="{{ item.status === 'active' ? 'success' : 'secondary' }}">
    {{ item.status }}
  </c-badge>
</td>
```

### Spinner (Loading)

```html
<c-spinner></c-spinner>
<c-spinner color="primary"></c-spinner>
<c-spinner size="sm"></c-spinner>
<c-spinner size="lg"></c-spinner>

<!-- With text -->
<div class="text-center">
  <c-spinner></c-spinner>
  <p class="mt-2">Loading...</p>
</div>
```

## Spacing Utilities

```html
<!-- Margin: m{side}-{size} -->
<div class="mb-3">margin-bottom: 1rem</div>
<div class="mt-2">margin-top: 0.5rem</div>
<div class="mx-auto">center horizontal</div>
<div class="ms-3">margin-start (left)</div>

<!-- Padding: p{side}-{size} -->
<div class="p-3">padding: 1rem</div>
<div class="px-4">padding left+right: 1.5rem</div>

<!-- Sizes: 1 (0.25rem), 2 (0.5rem), 3 (1rem), 4 (1.5rem), 5 (3rem) -->
```

## Text Utilities

```html
<!-- Alignment -->
<p class="text-center">Center</p>
<p class="text-end">Right</p>

<!-- Color -->
<p class="text-primary">Primary</p>
<p class="text-success">Success</p>
<p class="text-danger">Danger</p>
<p class="text-muted">Muted</p>

<!-- Size -->
<p class="fs-1">Largest</p>
<p class="fs-6">Smallest</p>

<!-- Weight -->
<p class="fw-bold">Bold</p>
<p class="fw-normal">Normal</p>

<!-- Decoration -->
<p class="text-decoration-underline">Underline</p>
```

## Flexbox Utilities

```html
<!-- Flex container -->
<div class="d-flex">Flex layout</div>
<div class="d-flex justify-content-between">Space between</div>
<div class="d-flex align-items-center">Vertical center</div>
<div class="d-flex flex-column">Stack vertically</div>

<!-- Gap between items -->
<div class="d-flex gap-3">
  <div>Item 1</div>
  <div>Item 2</div>
</div>

<!-- Grow/shrink -->
<div class="d-flex">
  <div class="flex-grow-1">Takes remaining space</div>
  <div>Fixed width</div>
</div>
```

## Responsive Display

```html
<!-- Hidden on mobile, visible on tablet+ -->
<div class="d-none d-md-block">Desktop only</div>

<!-- Visible on mobile, hidden on tablet+ -->
<div class="d-md-none">Mobile only</div>

<!-- Responsive padding -->
<div class="p-2 p-md-4">
  Small on mobile, larger on desktop
</div>

<!-- Responsive grid -->
<div class="row">
  <div class="col-12 col-md-6 col-lg-3">
    Responsive columns
  </div>
</div>
```

## Icons (CoreUI Icons Pro)

```html
<!-- Basic icon -->
<svg cIcon name="cilPlus"></svg>

<!-- With size -->
<svg cIcon name="cilPlus" size="lg"></svg>
<svg cIcon name="cilPlus" size="2xl"></svg>

<!-- With color -->
<svg cIcon name="cilCheckAlt" class="text-success"></svg>
<svg cIcon name="cilWarning" class="text-warning"></svg>

<!-- In buttons -->
<button cButton color="primary">
  <svg cIcon name="cilSave"></svg> Save
</button>

<!-- Common icons -->
cilPlus, cilPencil, cilTrash, cilCheckAlt, cilX, cilSearch,
cilImage, cilStar, cilDown, cilShare, cilSettings, cilMenu,
cilUser, cilSignOut, cilLockLocked, cilCloudUpload, cilEye,
cilZoom, cilWarning, cilInfo, cilCheckCircle
```

## Colors

```html
<!-- Primary colors -->
primary, secondary, success, danger, warning, info, light, dark

<!-- Examples -->
<button cButton color="primary">Primary</button>
<div class="bg-success">Success background</div>
<p class="text-danger">Danger text</p>
<c-badge color="warning">Warning</c-badge>

<!-- Background -->
<div class="bg-primary">Background</div>
<div class="bg-light">Light background</div>

<!-- Text color -->
<p class="text-primary">Colored text</p>
<p class="text-muted">Muted text</p>
```

## Common PhotoGallery Patterns

### Album List

```html
<div class="row g-3">
  <div class="col-md-4" *ngFor="let album of albums">
    <div cCard>
      <img cCardImage="top" [src]="album.preview">
      <div cCardBody>
        <h5 cCardTitle>{{ album.name }}</h5>
        <p cCardText class="text-muted">{{ album.photoCount }} photos</p>
        <div class="d-flex gap-2">
          <button cButton color="primary" size="sm">View</button>
          <button cButton color="info" size="sm">Edit</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

### Photo Grid

```html
<div class="row g-2">
  <div class="col-6 col-md-3" *ngFor="let photo of photos">
    <img [src]="photo.thumb" class="img-fluid rounded">
  </div>
</div>
```

### Form with Validation

```html
<form [formGroup]="form">
  <div cFormGroup>
    <label cFormLabel>Title</label>
    <input cFormControl type="text" formControlName="title"
           [class.is-invalid]="form.get('title')?.invalid && form.get('title')?.touched">
    <div class="invalid-feedback" *ngIf="form.get('title')?.invalid">
      Required
    </div>
  </div>
</form>
```

### Dashboard Layout

```html
<div class="wrapper">
  <app-sidebar></app-sidebar>
  <div class="body">
    <app-header></app-header>
    <div class="container-lg px-4">
      <router-outlet></router-outlet>
    </div>
  </div>
</div>
```

## Accessibility Checklist

- [x] Form inputs have labels (connected with `for`)
- [x] Error messages visible and associated
- [x] Color not only way to communicate (use icons/text)
- [x] Sufficient contrast (WCAG AA)
- [x] Images have alt text
- [x] Buttons have meaningful text/aria-label
- [x] Keyboard navigation works
- [x] Focus indicators visible

---

For complete component reference, see **SKILL.md**. For CoreUI docs: https://coreui.io/angular/docs/
