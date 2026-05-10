import { TestBed } from '@angular/core/testing';

import { CartItem, CartQuality, CartService, DEFAULT_CART_QUALITY } from './cart.service';

const STORAGE_PREFIX = 'photogallery-cart';
const CODE = 'TESTCODE';

function key(): string {
  return `${STORAGE_PREFIX}-${CODE}`;
}

function makeItem(id: string, quality: CartQuality = 'Medium'): CartItem {
  return { photoId: id, fileName: `${id}.jpg`, quality };
}

describe('CartService', () => {
  let service: CartService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(CartService);
    service.loadForCode('TESTCODE');
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('CartQuality value set', () => {
    it('accepts Original as a valid quality', () => {
      service = TestBed.inject(CartService);
      service.loadForCode(CODE);
      const ok = service.addItem(makeItem('p1', 'Original'));
      expect(ok).toBeTrue();
      expect(service.items[0].quality).toBe('Original');
    });

    it('treats (photoId, Original) as distinct from (photoId, High)', () => {
      service = TestBed.inject(CartService);
      service.loadForCode(CODE);
      service.addItem(makeItem('p1', 'High'));
      const ok = service.addItem(makeItem('p1', 'Original'));
      expect(ok).toBeTrue();
      expect(service.count).toBe(2);
    });
  });

  describe('addItem (existing behavior)', () => {
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
        makeItem('p4', 'Original'),
      ];
      localStorage.setItem(key(), JSON.stringify(seeded));

      service = TestBed.inject(CartService);
      service.loadForCode(CODE);

      expect(service.count).toBe(4);
      expect(service.items.map(i => i.quality)).toEqual(['Low', 'Medium', 'High', 'Original']);
    });

    it('coerces unknown quality strings to the default (Medium) without dropping the item', () => {
      const legacy = [
        { photoId: 'p1', fileName: 'p1.jpg', quality: 'Ultra' },        // unknown
        { photoId: 'p2', fileName: 'p2.jpg', quality: 'Low' },          // valid
        { photoId: 'p3', fileName: 'p3.jpg', quality: 'high' },         // case-mismatch -> coerced
      ];
      localStorage.setItem(key(), JSON.stringify(legacy));

      service = TestBed.inject(CartService);
      service.loadForCode(CODE);

      expect(service.count).toBe(3);
      expect(service.items.find(i => i.photoId === 'p1')!.quality).toBe(DEFAULT_CART_QUALITY);
      expect(service.items.find(i => i.photoId === 'p2')!.quality).toBe('Low');
      expect(service.items.find(i => i.photoId === 'p3')!.quality).toBe(DEFAULT_CART_QUALITY);
    });

    it('drops entries that lack the structural fields (photoId / fileName)', () => {
      const legacy = [
        { photoId: 'p1', fileName: 'p1.jpg', quality: 'Medium' },
        { fileName: 'orphan.jpg', quality: 'Low' },        // no photoId
        { photoId: 'p3', quality: 'High' },                // no fileName
        null,
        'not-an-object',
      ];
      localStorage.setItem(key(), JSON.stringify(legacy));

      service = TestBed.inject(CartService);
      service.loadForCode(CODE);

      expect(service.count).toBe(1);
      expect(service.items[0].photoId).toBe('p1');
    });

    it('persists the migrated form so the next load has no stale unknowns', () => {
      const legacy = [{ photoId: 'p1', fileName: 'p1.jpg', quality: 'Ultra' }];
      localStorage.setItem(key(), JSON.stringify(legacy));

      service = TestBed.inject(CartService);
      service.loadForCode(CODE);

      const persisted = JSON.parse(localStorage.getItem(key())!);
      expect(persisted[0].quality).toBe(DEFAULT_CART_QUALITY);
    });

    it('starts with an empty cart on corrupted JSON', () => {
      localStorage.setItem(key(), '{not json');
      service = TestBed.inject(CartService);
      service.loadForCode(CODE);
      expect(service.count).toBe(0);
    });
  });

  describe('addItems (transactional bulk add)', () => {
    it('adds multiple items and returns the count actually added', () => {
      const added = service.addItems([
        makeItem('p1'),
        makeItem('p2'),
        makeItem('p3'),
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
        makeItem('p2', 'Medium'),
      ]);
      expect(added).toBe(1);
      expect(service.count).toBe(2);
    });

    it('de-duplicates within the input batch', () => {
      const added = service.addItems([
        makeItem('p1'),
        makeItem('p1'),
        makeItem('p2'),
      ]);
      expect(added).toBe(2);
      expect(service.count).toBe(2);
    });

    it('enforces the 100-item cap and returns the truncated count', () => {
      const batch: CartItem[] = [];
      for (let i = 0; i < 120; i++) batch.push(makeItem(`p${i}`));
      const added = service.addItems(batch);
      expect(added).toBe(100);
      expect(service.count).toBe(100);
    });

    it('respects existing items when computing the cap', () => {
      const seed: CartItem[] = [];
      for (let i = 0; i < 90; i++) seed.push(makeItem(`s${i}`));
      service.addItems(seed);
      expect(service.count).toBe(90);

      const more: CartItem[] = [];
      for (let i = 0; i < 25; i++) more.push(makeItem(`m${i}`));
      const added = service.addItems(more);

      expect(added).toBe(10);
      expect(service.count).toBe(100);
    });

    it('persists the bulk add to localStorage', () => {
      service.addItems([makeItem('p1'), makeItem('p2')]);
      const raw = localStorage.getItem('photogallery-cart-TESTCODE');
      expect(raw).toBeTruthy();
      const parsed = JSON.parse(raw!);
      expect(parsed.length).toBe(2);
    });

    it('emits a single cart$ update for the whole batch', () => {
      const emissions: CartItem[][] = [];
      service.cart$.subscribe(items => emissions.push(items));
      emissions.length = 0;

      service.addItems([makeItem('p1'), makeItem('p2'), makeItem('p3')]);

      expect(emissions.length).toBe(1);
      expect(emissions[0].length).toBe(3);
    });
  });
});
