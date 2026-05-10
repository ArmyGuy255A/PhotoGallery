import { TestBed } from '@angular/core/testing';

import { CartService, CartItem } from './cart.service';

function makeItem(id: string, quality: 'Low' | 'Medium' | 'High' = 'Medium'): CartItem {
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
        makeItem('p1', 'Medium'), // duplicate
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
      // initial emission counted; reset
      emissions.length = 0;

      service.addItems([makeItem('p1'), makeItem('p2'), makeItem('p3')]);

      expect(emissions.length).toBe(1);
      expect(emissions[0].length).toBe(3);
    });
  });
});
