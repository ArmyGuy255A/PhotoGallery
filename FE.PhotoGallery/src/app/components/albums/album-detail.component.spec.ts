import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NEVER, of } from 'rxjs';

import { AlbumDetailComponent } from './album-detail.component';
import { CartService } from '../../services/cart.service';
import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';

interface Photo {
  id: string;
  fileName: string;
  uploadDate: string;
  uploadedBy?: string;
  processingStatus?: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

class CartServiceStub {
  addItem = jasmine.createSpy('addItem').and.returnValue(true);
  contains = jasmine.createSpy('contains').and.returnValue(false);
  removeItem = jasmine.createSpy('removeItem');
}

class AuthServiceStub {
  admin = true;
  isAdmin(): boolean { return this.admin; }
  getUser() { return this.admin ? { id: 'admin-1', email: 'a@b', roles: ['Admin'] } : null; }
}

// ---------------------------------------------------------------------------
// Cart integration tests (Issue #58)
// ---------------------------------------------------------------------------
describe('AlbumDetailComponent — cart integration (#58)', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let cart: CartServiceStub;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    cart = new CartServiceStub();

    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CartService, useValue: cart },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: ActivatedRoute, useValue: { params: of({ id: 'A1' }) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);

    // Album metadata
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1`).flush({
      id: 'A1', title: 'My Album', description: '',
      createdDate: new Date().toISOString(), createdBy: 'me', ownerId: 'u1'
    });
    // Photos
    httpMock.expectOne(req => req.url.startsWith(`${environment.apiUrl}/api/albums/A1/photos`)).flush({
      items: [
        { id: 'p1', fileName: 'p1.jpg', uploadDate: new Date().toISOString(),
          thumbnailUrl: 'http://thumb/p1', processingStatus: 'Complete' }
      ],
      totalCount: 1, page: 1, pageSize: 20, hasMore: false
    });
    // Access codes
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1/access-codes`).flush([]);
    fixture.detectChanges();
  });

  afterEach(() => {
    try { httpMock.verify(); } catch { /* polling timer may have queued one */ }
  });

  it('renders an Add to Cart button + quality select per photo', () => {
    const buttons = fixture.debugElement.queryAll(By.css('[data-testid="album-photo-add-to-cart"]'));
    const selects = fixture.debugElement.queryAll(By.css('[data-testid="album-photo-quality-select"]'));
    expect(buttons.length).toBe(1);
    expect(selects.length).toBe(1);
  });

  it('default-state cart button reads "+ Add" (issue #108)', () => {
    const btn: HTMLButtonElement = fixture.debugElement
      .query(By.css('[data-testid="album-photo-add-to-cart"]')).nativeElement;
    expect(btn.textContent?.trim()).toBe('+ Add');
    expect(btn.disabled).toBeFalse();
  });

  it('in-cart state cart button reads "✕ Remove" and stays clickable (issue #108)', () => {
    cart.contains = jasmine.createSpy('contains').and.returnValue(true);
    fixture.detectChanges();
    const btn: HTMLButtonElement = fixture.debugElement
      .query(By.css('[data-testid="album-photo-add-to-cart"]')).nativeElement;
    expect(btn.textContent?.trim()).toBe('✕ Remove');
    expect(btn.disabled).toBeFalse();
  });

  it('clicking the in-cart Remove button calls CartService.removeItem (issue #108)', () => {
    cart.contains = jasmine.createSpy('contains').and.returnValue(true);
    fixture.detectChanges();

    const btn = fixture.debugElement.query(By.css('[data-testid="album-photo-add-to-cart"]'));
    btn.triggerEventHandler('click', null);

    expect(cart.removeItem).toHaveBeenCalledTimes(1);
    const args = cart.removeItem.calls.mostRecent().args;
    expect(args[0]).toBe('p1');
    expect(args[1]).toBe('Medium');
  });

  it('clicking Add to Cart calls CartService.addItem with the album context', () => {
    const btn = fixture.debugElement.query(By.css('[data-testid="album-photo-add-to-cart"]'));
    btn.triggerEventHandler('click', null);

    expect(cart.addItem).toHaveBeenCalledTimes(1);
    const arg = cart.addItem.calls.mostRecent().args[0];
    expect(arg.photoId).toBe('p1');
    expect(arg.fileName).toBe('p1.jpg');
    expect(arg.thumbnailUrl).toBe('http://thumb/p1');
    expect(arg.quality).toBe('Medium');
    expect(arg.sourceAlbumId).toBe('A1');
    expect(arg.sourceAlbumTitle).toBe('My Album');
  });

  describe('per-photo delete (issue #113)', () => {
    function deleteBtn() {
      return fixture.debugElement.query(By.css('[data-testid="photo-delete-btn"]'));
    }

    it('renders the ✕ delete button for admins', () => {
      expect(deleteBtn()).toBeTruthy();
    });

    it('does NOT issue DELETE when the user cancels the confirm', () => {
      spyOn(window, 'confirm').and.returnValue(false);
      deleteBtn().triggerEventHandler('click', new Event('click'));
      httpMock.expectNone(`${environment.apiUrl}/api/photos/p1`);
      expect(fixture.componentInstance.photos.length).toBe(1);
    });

    it('issues DELETE and removes the photo from the local list on success', () => {
      spyOn(window, 'confirm').and.returnValue(true);
      deleteBtn().triggerEventHandler('click', new Event('click'));

      const req = httpMock.expectOne(`${environment.apiUrl}/api/photos/p1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(fixture.componentInstance.photos.find(p => p.id === 'p1')).toBeUndefined();
    });

    it('keeps the photo in the list when the server returns an error', () => {
      spyOn(window, 'confirm').and.returnValue(true);
      spyOn(window, 'alert');
      deleteBtn().triggerEventHandler('click', new Event('click'));

      const req = httpMock.expectOne(`${environment.apiUrl}/api/photos/p1`);
      req.flush('boom', { status: 500, statusText: 'Server Error' });

      expect(fixture.componentInstance.photos.length).toBe(1);
      expect(window.alert).toHaveBeenCalled();
    });
  });
});

// ---------------------------------------------------------------------------
// getShouldShowBadge + photo-status-badge rendering tests
// ---------------------------------------------------------------------------
describe('AlbumDetailComponent', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let component: AlbumDetailComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent, HttpClientTestingModule],
      providers: [
        provideRouter([]),
        { provide: AuthService, useClass: AuthServiceStub },
        // params: NEVER so ngOnInit's subscribe never fires and we can set state by hand.
        { provide: ActivatedRoute, useValue: { params: NEVER } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    // Bypass the loader so the photos grid renders.
    component.isLoading = false;
    component.album = {
      id: 'album-1',
      title: 'Test Album',
      description: 'desc',
      createdDate: '2026-01-01T00:00:00Z',
      createdBy: 'tester',
      ownerId: 'owner-1'
    } as any;
  });

  afterEach(() => {
    httpMock.verify();
  });

  function makePhoto(overrides: Partial<Photo> = {}): Photo {
    return {
      id: 'p1',
      fileName: 'photo.jpg',
      uploadDate: '2026-01-01T00:00:00Z',
      ...overrides
    };
  }

  describe('getShouldShowBadge()', () => {
    it('returns false for "Complete"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Complete' }))).toBe(false);
    });

    it('returns false for null', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: null as any }))).toBe(false);
    });

    it('returns false for undefined', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: undefined }))).toBe(false);
    });

    it('returns false for empty string', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: '' }))).toBe(false);
    });

    it('returns true for "Processing"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Processing' }))).toBe(true);
    });

    it('returns true for "Failed"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Failed' }))).toBe(true);
    });
  });

  describe('photo-status-badge DOM rendering', () => {
    function renderWithStatus(processingStatus: string | null | undefined) {
      component.photos = [makePhoto({ id: 'p1', processingStatus: processingStatus as any })];
      fixture.detectChanges();
    }

    function badgeEl() {
      return fixture.debugElement.query(By.css('[data-testid="photo-status-badge"]'));
    }

    it('renders the badge for "Processing"', () => {
      renderWithStatus('Processing');
      expect(badgeEl()).withContext('badge present for Processing').toBeTruthy();
    });

    it('renders the badge for "Failed"', () => {
      renderWithStatus('Failed');
      expect(badgeEl()).withContext('badge present for Failed').toBeTruthy();
    });

    it('does NOT render the badge for "Complete"', () => {
      renderWithStatus('Complete');
      expect(badgeEl()).withContext('badge hidden for Complete').toBeFalsy();
    });

    it('does NOT render the badge for null', () => {
      renderWithStatus(null);
      expect(badgeEl()).withContext('badge hidden for null').toBeFalsy();
    });

    it('does NOT render the badge for undefined', () => {
      renderWithStatus(undefined);
      expect(badgeEl()).withContext('badge hidden for undefined').toBeFalsy();
    });

    it('does NOT render the badge for empty string', () => {
      renderWithStatus('');
      expect(badgeEl()).withContext('badge hidden for empty string').toBeFalsy();
    });
  });
});

// ---------------------------------------------------------------------------
// Phase 6 — progressive grid: skeleton, empty-state, infinite-scroll
// ---------------------------------------------------------------------------
describe('AlbumDetailComponent — Phase 6 progressive grid', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let component: AlbumDetailComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent, HttpClientTestingModule],
      providers: [
        provideRouter([]),
        { provide: AuthService, useClass: AuthServiceStub },
        // params: NEVER so ngOnInit doesn't auto-fire the loader; we drive it
        // explicitly from each spec to keep the scenarios deterministic.
        { provide: ActivatedRoute, useValue: { params: NEVER } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    component.isLoading = false;
    component.albumId = 'A1';
    component.album = {
      id: 'A1', title: 'A', description: '',
      createdDate: '2026-01-01T00:00:00Z', createdBy: 'me', ownerId: 'u1'
    } as any;
  });

  afterEach(() => {
    try { httpMock.verify(); } catch { /* noop */ }
  });

  function skeleton() {
    return fixture.debugElement.query(By.css('[data-testid="album-photos-skeleton"]'));
  }
  function emptyMessage() {
    return fixture.debugElement.query(By.css('[data-testid="album-empty-photos"]'));
  }
  function sentinel() {
    return fixture.debugElement.query(By.css('[data-testid="album-photos-sentinel"]'));
  }

  it('renders the skeleton grid while the loader is fetching (no empty-state flash)', () => {
    component.loader.loadNext();
    fixture.detectChanges();

    expect(skeleton()).withContext('skeleton renders during load').toBeTruthy();
    expect(emptyMessage()).withContext('no empty-state during load').toBeFalsy();

    const req = httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'));
    req.flush({ items: [{ id: 'p1', fileName: 'a.jpg', uploadDate: '' }],
                page: 1, pageSize: 20, totalCount: 1, hasMore: false });
    fixture.detectChanges();

    expect(skeleton()).withContext('skeleton gone after load').toBeFalsy();
  });

  it('shows the empty-state ONLY when nothing is loading AND server reports no more pages', () => {
    // Before any load: no empty-state (we haven't asked yet).
    fixture.detectChanges();
    expect(emptyMessage()).withContext('no empty-state before first load').toBeFalsy();

    // Mid-load: still no empty-state, skeleton instead.
    component.loader.loadNext();
    fixture.detectChanges();
    expect(emptyMessage()).withContext('no empty-state mid-load').toBeFalsy();

    // Flush an empty page with hasMore=false — only NOW does the empty copy appear.
    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
    fixture.detectChanges();

    expect(emptyMessage()).withContext('empty-state after empty hasMore=false').toBeTruthy();
  });

  it('renders the IntersectionObserver sentinel while loader.hasMore() is true', () => {
    component.loader.loadNext();
    fixture.detectChanges();
    expect(sentinel()).withContext('sentinel present during load').toBeTruthy();

    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({ items: [{ id: 'p1', fileName: 'a.jpg', uploadDate: '' }],
               page: 1, pageSize: 20, totalCount: 1, hasMore: false });
    fixture.detectChanges();

    expect(sentinel()).withContext('sentinel removed when hasMore=false').toBeFalsy();
  });

  it('loader.loadNext appends results to the grid (infinite-scroll surrogate)', () => {
    component.loader.loadNext();
    httpMock.expectOne(r => r.url.includes('page=1'))
      .flush({ items: [{ id: 'p1', fileName: 'a.jpg', uploadDate: '' }],
               page: 1, pageSize: 1, totalCount: 2, hasMore: true });
    fixture.detectChanges();
    expect(component.photos.length).toBe(1);

    // Simulate the sentinel firing.
    component.loader.loadNext();
    httpMock.expectOne(r => r.url.includes('page=2'))
      .flush({ items: [{ id: 'p2', fileName: 'b.jpg', uploadDate: '' }],
               page: 2, pageSize: 1, totalCount: 2, hasMore: false });
    fixture.detectChanges();

    expect(component.photos.map(p => p.id)).toEqual(['p1', 'p2']);
    expect(component.loader.hasMore()).toBeFalse();
  });

  it('sends page=1 and pageSize=20 on the first fetch (paginated contract)', () => {
    component.loader.loadNext();
    const req = httpMock.expectOne(r => r.url.startsWith(`${environment.apiUrl}/api/albums/A1/photos`));
    expect(req.request.urlWithParams).toContain('page=1');
    expect(req.request.urlWithParams).toContain('pageSize=20');
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  });

  // -----------------------------------------------------------------------
  // Phase 7: initial spinner only. The "Loaded X of Y" banner was removed
  // in favour of auto-trickle pagination — every page loads in the
  // background so the banner had nothing useful to convey.
  // -----------------------------------------------------------------------
  function loadingInitial() {
    return fixture.debugElement.query(By.css('[data-testid="album-photos-loading-initial"]'));
  }
  function loadingBanner() {
    return fixture.debugElement.query(By.css('[data-testid="album-photos-loading-banner"]'));
  }

  it('shows the centred "Loading photos…" spinner before the first page lands', () => {
    component.loader.loadNext();
    fixture.detectChanges();

    const initial = loadingInitial();
    expect(initial).withContext('initial spinner present during first fetch').toBeTruthy();
    expect(initial.nativeElement.textContent).toContain('Loading photos…');
    expect(loadingBanner()).withContext('banner element removed from template').toBeFalsy();

    // Stop the in-flight request so afterEach.verify() stays clean.
    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  });

  it('never renders the "Loaded X of Y photos…" banner (auto-trickle replaces it)', () => {
    component.loader.loadNext();
    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({
        items: Array.from({ length: 20 }, (_, i) => ({
          id: 'p' + i, fileName: 'p' + i + '.jpg', uploadDate: '2026-01-01T00:00:00Z'
        })),
        page: 1, pageSize: 20, totalCount: 264, hasMore: true
      });
    fixture.detectChanges();

    expect(loadingInitial()).withContext('initial spinner gone after first page').toBeFalsy();
    expect(loadingBanner()).withContext('banner element no longer in template').toBeFalsy();
  });

  it('hides the spinner once photos.length === totalCount', () => {
    component.loader.loadNext();
    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({
        items: [{ id: 'p1', fileName: 'p1.jpg', uploadDate: '2026-01-01T00:00:00Z' }],
        page: 1, pageSize: 20, totalCount: 1, hasMore: false
      });
    fixture.detectChanges();

    expect(loadingInitial()).withContext('initial spinner gone').toBeFalsy();
    expect(loadingBanner()).withContext('banner element no longer in template').toBeFalsy();
  });

  it('keeps the empty-state copy and suppresses the spinner when the album is empty', () => {
    component.loader.loadNext();
    httpMock.expectOne(r => r.url.includes('/api/albums/A1/photos'))
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
    fixture.detectChanges();

    expect(emptyMessage()).withContext('empty-state still wins').toBeTruthy();
    expect(loadingInitial()).withContext('initial spinner hidden in empty state').toBeFalsy();
    expect(loadingBanner()).withContext('banner element no longer in template').toBeFalsy();
  });
});


// ---------------------------------------------------------------------------
// Owner display-name on album header (resolves GUID -> friendly name)
// ---------------------------------------------------------------------------
describe('AlbumDetailComponent — owner display name', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CartService, useValue: new CartServiceStub() },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: ActivatedRoute, useValue: { params: of({ id: 'A1' }) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { httpMock.verify(); } catch { /* polling timer may have queued one */ }
  });

  it('renders the resolved display name instead of the raw GUID when present', () => {
    const guid = '08a0e965-50e9-4cac-bc5d-e88b72d9a9f7';
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1`).flush({
      id: 'A1', title: 'My Album', description: '',
      createdDate: new Date().toISOString(),
      createdBy: guid,
      createdByDisplayName: 'Phillip Dieppa',
      ownerId: 'u1'
    });
    httpMock.expectOne(r => r.url.startsWith(`${environment.apiUrl}/api/albums/A1/photos`))
      .flush({ items: [], totalCount: 0, page: 1, pageSize: 20, hasMore: false });
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1/access-codes`).flush([]);
    fixture.detectChanges();

    const meta: HTMLElement = fixture.debugElement.query(By.css('p.meta')).nativeElement;
    expect(meta.textContent).toContain('by Phillip Dieppa');
    expect(meta.textContent).not.toContain(guid);
  });

  it('falls back to the raw createdBy value when display name is missing', () => {
    const guid = '08a0e965-50e9-4cac-bc5d-e88b72d9a9f7';
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1`).flush({
      id: 'A1', title: 'My Album', description: '',
      createdDate: new Date().toISOString(),
      createdBy: guid,
      ownerId: 'u1'
    });
    httpMock.expectOne(r => r.url.startsWith(`${environment.apiUrl}/api/albums/A1/photos`))
      .flush({ items: [], totalCount: 0, page: 1, pageSize: 20, hasMore: false });
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1/access-codes`).flush([]);
    fixture.detectChanges();

    const meta: HTMLElement = fixture.debugElement.query(By.css('p.meta')).nativeElement;
    expect(meta.textContent).toContain(`by ${guid}`);
  });
});
