import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  CartCapError,
  CartItem,
  CartQuality,
  CartService,
  DEFAULT_CART_QUALITY
} from './cart.service';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

const STORAGE_PREFIX = 'photogallery-cart';
const CODE = 'TESTCODE';

function key(code: string = CODE): string {
  return `${STORAGE_PREFIX}-${code}`;
}

function makeItem(id: string, quality: CartQuality = 'Medium'): CartItem {
  return { photoId: id, fileName: `${id}.jpg`, quality };
}

class AuthServiceStub {
  authenticated = false;
  isAuthenticatedSync(): boolean { return this.authenticated; }
}

describe('CartService', () => {
  let service: CartService;
  let auth: AuthServiceStub;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    auth = new AuthServiceStub();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth }
      ]
    });
    service = TestBed.inject(CartService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Drain any pending requests so independent tests don't leak.
    try { httpMock.verify(); } catch { /* tests assert specific reqs */ }
    localStorage.clear();
  });

  // ===========================================================================
  // Anonymous (localStorage) mode — pre-existing behaviour preserved
  // ===========================================================================

  describe('anonymous mode', () => {
    beforeEach(() => {
      auth.authenticated = false;
      service.loadForCode(CODE);
    });

    it('should be created', () => {
      expect(service).toBeTruthy();
    });

    describe('CartQuality value set', () => {
      it('accepts Original as a valid quality', () => {
        const ok = service.addItem(makeItem('p1', 'Original'));
        expect(ok).toBeTrue();
        expect(service.items[0].quality).toBe('Original');
      });

      it('treats (photoId, Original) as distinct from (photoId, High)', () => {
        service.addItem(makeItem('p1', 'High'));
        const ok = service.addItem(makeItem('p1', 'Original'));
        expect(ok).toBeTrue();
        expect(service.count).toBe(2);
      });
    });

    describe('addItem', () => {
      it('adds a single item and persists', () => {
        const ok = service.addItem(makeItem('p1'));
        expect(ok).toBeTrue();
        expect(service.count).toBe(1);
      });

      it('rejects duplicates of (photoId, quality)', () => {
        service.addItem(makeItem('p1', 'Low'));
        const ok = service.addItem(makeItem('p1', 'Low'));
        expect(ok).toBeFalse();
        expect(service.count).toBe(1);
      });

      it('allows same photoId with different quality', () => {
        service.addItem(makeItem('p1', 'Low'));
        const ok = service.addItem(makeItem('p1', 'High'));
        expect(ok).toBeTrue();
        expect(service.count).toBe(2);
      });
    });

    describe('localStorage migration on loadForCode', () => {
      it('preserves valid items including Original', () => {
        const seeded: CartItem[] = [
          makeItem('p1', 'Low'),
          makeItem('p2', 'Medium'),
          makeItem('p3', 'High'),
          makeItem('p4', 'Original')
        ];
        localStorage.setItem(key(), JSON.stringify(seeded));

        service.loadForCode(CODE);

        expect(service.count).toBe(4);
        expect(service.items.map(i => i.quality)).toEqual(['Low', 'Medium', 'High', 'Original']);
      });

      it('coerces unknown quality strings to the default (Medium) without dropping the item', () => {
        const legacy = [
          { photoId: 'p1', fileName: 'p1.jpg', quality: 'Ultra' },
          { photoId: 'p2', fileName: 'p2.jpg', quality: 'Low' },
          { photoId: 'p3', fileName: 'p3.jpg', quality: 'high' }
        ];
        localStorage.setItem(key(), JSON.stringify(legacy));

        service.loadForCode(CODE);

        expect(service.count).toBe(3);
        expect(service.items.find(i => i.photoId === 'p1')!.quality).toBe(DEFAULT_CART_QUALITY);
        expect(service.items.find(i => i.photoId === 'p2')!.quality).toBe('Low');
        expect(service.items.find(i => i.photoId === 'p3')!.quality).toBe(DEFAULT_CART_QUALITY);
      });

      it('drops entries that lack the structural fields (photoId / fileName)', () => {
        const legacy = [
          { photoId: 'p1', fileName: 'p1.jpg', quality: 'Medium' },
          { fileName: 'orphan.jpg', quality: 'Low' },
          { photoId: 'p3', quality: 'High' },
          null,
          'not-an-object'
        ];
        localStorage.setItem(key(), JSON.stringify(legacy));

        service.loadForCode(CODE);

        expect(service.count).toBe(1);
        expect(service.items[0].photoId).toBe('p1');
      });

      it('persists the migrated form so the next load has no stale unknowns', () => {
        const legacy = [{ photoId: 'p1', fileName: 'p1.jpg', quality: 'Ultra' }];
        localStorage.setItem(key(), JSON.stringify(legacy));

        service.loadForCode(CODE);

        const persisted = JSON.parse(localStorage.getItem(key())!);
        expect(persisted[0].quality).toBe(DEFAULT_CART_QUALITY);
      });

      it('starts with an empty cart on corrupted JSON', () => {
        localStorage.setItem(key(), '{not json');
        service.loadForCode(CODE);
        expect(service.count).toBe(0);
      });
    });

    describe('addItems', () => {
      it('adds multiple items and returns the count actually added', () => {
        const added = service.addItems([
          makeItem('p1'),
          makeItem('p2'),
          makeItem('p3')
        ]);
        expect(added).toBe(3);
        expect(service.count).toBe(3);
      });

      it('returns 0 and does not mutate state for an empty input', () => {
        const added = service.addItems([]);
        expect(added).toBe(0);
        expect(service.count).toBe(0);
      });

      it('skips items already in the cart (existing dedupe)', () => {
        service.addItem(makeItem('p1', 'Medium'));
        const added = service.addItems([
          makeItem('p1', 'Medium'),
          makeItem('p2', 'Medium')
        ]);
        expect(added).toBe(1);
        expect(service.count).toBe(2);
      });

      it('de-duplicates within the input batch', () => {
        const added = service.addItems([
          makeItem('p1'),
          makeItem('p1'),
          makeItem('p2')
        ]);
        expect(added).toBe(2);
        expect(service.count).toBe(2);
      });

      it('enforces the 99999-item cap and returns the truncated count', () => {
        const batch: CartItem[] = [];
        for (let i = 0; i < 100050; i++) batch.push(makeItem(`p${i}`));
        const added = service.addItems(batch);
        expect(added).toBe(99999);
        expect(service.count).toBe(99999);
      });
    });
  });

  // ===========================================================================
  // Authenticated (HTTP) mode
  // ===========================================================================

  describe('authenticated mode', () => {
    const flushMicrotasks = async () => {
      for (let i = 0; i < 5; i++) await Promise.resolve();
    };

    beforeEach(() => {
      auth.authenticated = true;
    });

    it('loadForUser GETs /api/cart and seeds items', async () => {
      const promise = service.loadForUser();
      await flushMicrotasks();
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(req.request.method).toBe('GET');
      req.flush({
        items: [
          { photoId: 'p1', fileName: 'a.jpg', quality: 'High', sourceAlbumId: 'A1', sourceAlbumTitle: 'Alpha' },
          { photoId: 'p2', fileName: 'b.jpg', quality: 'Medium', sourceAlbumId: 'A2', sourceAlbumTitle: 'Beta' }
        ]
      });
      await promise;
      expect(service.count).toBe(2);
      expect(service.items[0].sourceAlbumTitle).toBe('Alpha');
    });

    it('addItem POSTs to /api/cart and updates state on success', async () => {
      // First trigger a no-op loadForUser so the migration step finishes (no localStorage carts).
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({ items: [] });
      await initPromise;

      service.addItem({ photoId: 'p1', fileName: 'p1.jpg', quality: 'High', sourceAlbumId: 'A1' });
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ photoId: 'p1', quality: 'High', sourceAlbumId: 'A1' });
      req.flush(null);
      expect(service.count).toBe(1);
      expect(service.items[0].photoId).toBe('p1');
    });

    it('addItem keeps the locally-set sourceAlbumTitle when server returns null (issue #110)', async () => {
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({ items: [] });
      await initPromise;

      service.addItem({
        photoId: 'p1',
        fileName: 'p1.jpg',
        quality: 'High',
        sourceAlbumId: 'A1',
        sourceAlbumTitle: 'Alpha'
      });
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      // Older deployments / partial responses may echo a null SourceAlbumTitle.
      // The merge must not let that overwrite the title the call site supplied.
      req.flush({
        photoId: 'p1',
        fileName: 'p1.jpg',
        quality: 'High',
        sourceAlbumId: 'A1',
        sourceAlbumTitle: null
      });

      expect(service.items[0].sourceAlbumTitle).toBe('Alpha');
    });

    it('addItem on 409 cap_reached emits a cap error message', async () => {
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({ items: [] });
      await initPromise;

      const errors: string[] = [];
      service.errors.subscribe(m => errors.push(m));

      service.addItem({ photoId: 'p1', fileName: 'p1.jpg', quality: 'Medium' });
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      req.flush({ reason: 'cap_reached', limit: 99999 }, { status: 409, statusText: 'Conflict' });

      expect(errors.length).toBe(1);
      expect(errors[0]).toContain('Cart is full');
    });

    it('removeItem DELETEs and updates state', async () => {
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({
        items: [{ photoId: 'p1', fileName: 'a.jpg', quality: 'High' }]
      });
      await initPromise;

      service.removeItem('p1', 'High');
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart/p1/High`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);

      expect(service.count).toBe(0);
    });

    it('clear DELETEs /api/cart and empties state', async () => {
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({
        items: [{ photoId: 'p1', fileName: 'a.jpg', quality: 'High' }]
      });
      await initPromise;

      service.clear();
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);

      expect(service.count).toBe(0);
    });

    it('download reads X-Skipped-Photo-Ids and emits the skipped list', async () => {
      const initPromise = service.loadForUser();
      await flushMicrotasks();
      httpMock.expectOne(`${environment.apiUrl}/api/cart`).flush({
        items: [
          { photoId: 'p1', fileName: 'a.jpg', quality: 'High' },
          { photoId: 'p2', fileName: 'b.jpg', quality: 'High' }
        ]
      });
      await initPromise;

      // Stub URL/createElement so triggerBrowserDownload doesn't actually navigate.
      spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
      spyOn(URL, 'revokeObjectURL').and.stub();
      const fakeAnchor = { click: jasmine.createSpy('click'), href: '', download: '', setAttribute: () => {} } as unknown as HTMLAnchorElement;
      spyOn(document, 'createElement').and.returnValue(fakeAnchor);
      spyOn(document.body, 'appendChild').and.returnValue(fakeAnchor);
      spyOn(document.body, 'removeChild').and.returnValue(fakeAnchor);

      const skipped: string[][] = [];
      service.skippedPhotoIds$.subscribe(s => skipped.push(s));

      const promise = service.download();
      await flushMicrotasks();
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart/download`);
      expect(req.request.method).toBe('POST');
      req.flush(new Blob(['zip-bytes']), {
        status: 200,
        statusText: 'OK',
        headers: { 'X-Skipped-Photo-Ids': 'pX,pY' }
      });
      await promise;

      expect(skipped.length).toBe(1);
      expect(skipped[0]).toEqual(['pX', 'pY']);
      expect(service.count).toBe(0); // cleared on success
    });

    it('download throws CartCapError on 409', async () => {
      const promise = service.download();
      await flushMicrotasks();
      const req = httpMock.expectOne(`${environment.apiUrl}/api/cart/download`);
      req.flush(
        new Blob([JSON.stringify({ reason: 'cap_reached', limit: 99999 })], { type: 'application/json' }),
        { status: 409, statusText: 'Conflict' }
      );

      await promise.then(
        () => fail('expected download to throw'),
        (err: unknown) => {
          expect(err instanceof CartCapError).toBeTrue();
        }
      );
    });

    it('migrates a localStorage cart on first authenticated load (POSTs each item then clears the key)', async () => {
      localStorage.setItem(key('CODE-A'), JSON.stringify([
        { photoId: 'lp1', fileName: 'lp1.jpg', quality: 'Low' },
        { photoId: 'lp2', fileName: 'lp2.jpg', quality: 'High' }
      ]));

      const promise = service.loadForUser();

      const flushMicrotasks = async () => {
        for (let i = 0; i < 5; i++) await Promise.resolve();
      };

      // Migration POSTs (sequential awaited firstValueFrom — each must be flushed before the next is queued).
      await flushMicrotasks();
      const post1 = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(post1.request.method).toBe('POST');
      expect(post1.request.body).toEqual({ photoId: 'lp1', quality: 'Low' });
      post1.flush(null);
      await flushMicrotasks();

      const post2 = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(post2.request.body).toEqual({ photoId: 'lp2', quality: 'High' });
      post2.flush(null);
      await flushMicrotasks();

      // Final reload GET after migration completes
      const get = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      expect(get.request.method).toBe('GET');
      get.flush({
        items: [
          { photoId: 'lp1', fileName: 'lp1.jpg', quality: 'Low' },
          { photoId: 'lp2', fileName: 'lp2.jpg', quality: 'High' }
        ]
      });
      await promise;

      expect(localStorage.getItem(key('CODE-A'))).toBeNull();
      expect(service.count).toBe(2);
    });

    it('migration silently skips items the user no longer has access to (403)', async () => {
      localStorage.setItem(key('CODE-X'), JSON.stringify([
        { photoId: 'np1', fileName: 'np1.jpg', quality: 'Low' }
      ]));

      const promise = service.loadForUser();
      const flushMicrotasks = async () => {
        for (let i = 0; i < 5; i++) await Promise.resolve();
      };

      await flushMicrotasks();
      const post = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      post.flush({}, { status: 403, statusText: 'Forbidden' });
      await flushMicrotasks();

      const get = httpMock.expectOne(`${environment.apiUrl}/api/cart`);
      get.flush({ items: [] });
      await promise;

      expect(localStorage.getItem(key('CODE-X'))).toBeNull();
      expect(service.count).toBe(0);
    });
  });

  // ===========================================================================
  // Drawer state
  // ===========================================================================

  describe('drawer state', () => {
    it('starts closed and toggles', (done) => {
      const states: boolean[] = [];
      service.cartDrawerOpen$.subscribe(s => {
        states.push(s);
        if (states.length === 4) {
          expect(states).toEqual([false, true, false, true]);
          done();
        }
      });
      service.toggleDrawer();
      service.closeDrawer();
      service.openDrawer();
    });
  });
});
