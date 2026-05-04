import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export type CartQuality = 'Low' | 'Medium' | 'High';

export interface CartItem {
  photoId: string;
  fileName: string;
  thumbnailUrl?: string;
  quality: CartQuality;
}

const STORAGE_KEY_PREFIX = 'photogallery-cart';
const MAX_CART_SIZE = 100;

/**
 * Client-side shopping cart for unauthenticated users (access-code flow).
 *
 * State is reactive (BehaviorSubject) and persisted to localStorage scoped per
 * access code so that visiting different codes in the same browser doesn't
 * cross-contaminate carts.
 */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly items$ = new BehaviorSubject<CartItem[]>([]);
  private currentCode: string | null = null;

  /** Emits the current cart state. */
  get cart$(): Observable<CartItem[]> {
    return this.items$.asObservable();
  }

  /** Synchronous snapshot of cart items. */
  get items(): CartItem[] {
    return [...this.items$.value];
  }

  /** Hydrate the cart for a specific access code from localStorage. */
  loadForCode(code: string): void {
    this.currentCode = code;
    const raw = localStorage.getItem(this.storageKey(code));
    if (!raw) {
      this.items$.next([]);
      return;
    }
    try {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        this.items$.next(parsed.filter(this.isValidItem));
      } else {
        this.items$.next([]);
      }
    } catch {
      // Corrupted localStorage — start fresh
      this.items$.next([]);
    }
  }

  /**
   * Add a photo to the cart at the given quality. If the same (photoId, quality)
   * is already present, this is a no-op. Returns true if added, false if at limit
   * or duplicate.
   */
  addItem(item: CartItem): boolean {
    const existing = this.items$.value;
    const isDuplicate = existing.some(
      i => i.photoId === item.photoId && i.quality === item.quality
    );
    if (isDuplicate) return false;
    if (existing.length >= MAX_CART_SIZE) return false;
    const updated = [...existing, item];
    this.items$.next(updated);
    this.persist();
    return true;
  }

  /** Remove an item by (photoId, quality). */
  removeItem(photoId: string, quality: CartQuality): void {
    const updated = this.items$.value.filter(
      i => !(i.photoId === photoId && i.quality === quality)
    );
    this.items$.next(updated);
    this.persist();
  }

  /** Update the quality of an existing item. */
  updateQuality(photoId: string, oldQuality: CartQuality, newQuality: CartQuality): void {
    if (oldQuality === newQuality) return;
    const updated = this.items$.value.map(i =>
      (i.photoId === photoId && i.quality === oldQuality)
        ? { ...i, quality: newQuality }
        : i
    );
    this.items$.next(updated);
    this.persist();
  }

  /** Empty the cart for the current code. */
  clear(): void {
    this.items$.next([]);
    this.persist();
  }

  /** True if a given (photoId, quality) is in the cart. */
  contains(photoId: string, quality: CartQuality): boolean {
    return this.items$.value.some(i => i.photoId === photoId && i.quality === quality);
  }

  /** Count of distinct items in the cart. */
  get count(): number {
    return this.items$.value.length;
  }

  private storageKey(code: string): string {
    return `${STORAGE_KEY_PREFIX}-${code}`;
  }

  private persist(): void {
    if (!this.currentCode) return;
    try {
      localStorage.setItem(
        this.storageKey(this.currentCode),
        JSON.stringify(this.items$.value)
      );
    } catch {
      // localStorage may be full or disabled — ignore, cart still works in-memory
    }
  }

  private isValidItem(value: any): value is CartItem {
    return value
      && typeof value.photoId === 'string'
      && typeof value.fileName === 'string'
      && (value.quality === 'Low' || value.quality === 'Medium' || value.quality === 'High');
  }
}
