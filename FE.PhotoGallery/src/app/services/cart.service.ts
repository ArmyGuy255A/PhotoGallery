import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

/**
 * Quality levels accepted by the cart download flow. Mirrors
 * <c>PhotoGallery.Enums.QualityType</c> minus <c>Thumbnail</c>, which is
 * preview-only and rejected server-side. <c>Original</c> resolves to the
 * untouched upload (<c>original.jpg</c>) and is delivered unwatermarked.
 */
export type CartQuality = 'Low' | 'Medium' | 'High' | 'Original';

const VALID_QUALITIES: ReadonlyArray<CartQuality> = ['Low', 'Medium', 'High', 'Original'];

/** Fallback used when migrating legacy carts that contain unknown quality values. */
export const DEFAULT_CART_QUALITY: CartQuality = 'Medium';

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
        // Migration: legacy carts may contain qualities outside the current set
        // (e.g. saved before 'Original' was a valid value, or future values that
        // got rolled back). Drop fully invalid entries; coerce items whose only
        // problem is an unknown quality string back to 'Medium' so the user
        // doesn't lose the photo from their cart.
        const migrated = parsed
          .map((v: unknown) => this.migrateItem(v))
          .filter((v): v is CartItem => v !== null);
        this.items$.next(migrated);
        // Persist the migrated form so the next load is already clean.
        if (migrated.length !== parsed.length || this.didMigrate(parsed, migrated)) {
          this.persist();
        }
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
      && VALID_QUALITIES.includes(value.quality);
  }

  /**
   * Returns a normalised <see cref="CartItem"/> for a value loaded from localStorage,
   * or <c>null</c> if the value is unsalvageable (missing photoId / fileName).
   * Items with an unknown quality string are coerced to <see cref="DEFAULT_CART_QUALITY"/>.
   */
  private migrateItem(value: any): CartItem | null {
    if (!value || typeof value.photoId !== 'string' || typeof value.fileName !== 'string') {
      return null;
    }
    const quality: CartQuality = VALID_QUALITIES.includes(value.quality)
      ? value.quality
      : DEFAULT_CART_QUALITY;
    return {
      photoId: value.photoId,
      fileName: value.fileName,
      thumbnailUrl: typeof value.thumbnailUrl === 'string' ? value.thumbnailUrl : undefined,
      quality,
    };
  }

  /** True if the parsed/migrated arrays disagree on any quality value. */
  private didMigrate(parsed: any[], migrated: CartItem[]): boolean {
    if (parsed.length !== migrated.length) return true;
    for (let i = 0; i < parsed.length; i++) {
      if (parsed[i]?.quality !== migrated[i].quality) return true;
    }
    return false;
  }
}
