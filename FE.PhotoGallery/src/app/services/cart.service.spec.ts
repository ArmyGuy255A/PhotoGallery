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
  });

  afterEach(() => {
    localStorage.clear();
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
});
