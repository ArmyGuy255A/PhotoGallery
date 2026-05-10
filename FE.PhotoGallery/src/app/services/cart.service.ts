import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

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

/**
 * Unified cart item shape. Server-side fields (sourceAlbumId, sourceAlbumTitle,
 * addedAt) are optional so the same shape works for the anonymous (per-code,
 * single-album) localStorage path too.
 */
export interface CartItem {
  photoId: string;
  fileName: string;
  thumbnailUrl?: string;
  quality: CartQuality;
  sourceAlbumId?: string;
  sourceAlbumTitle?: string;
  addedAt?: string;
}

/** Server response envelope for GET /api/cart. */
interface CartResponse {
  items: CartItem[];
}

const STORAGE_KEY_PREFIX = 'photogallery-cart';
const MAX_CART_SIZE = 100;

/** Error code thrown by service when the server returns a 409 cap_reached. */
export const CART_CAP_REACHED = 'cart_cap_reached';

export class CartCapError extends Error {
  readonly code = CART_CAP_REACHED;
  constructor(public readonly limit: number = MAX_CART_SIZE) {
    super(`Cart is full (${limit} items max).`);
  }
}

/**
 * Dual-mode shopping cart.
 *
 * - When the user is authenticated, mutations call `/api/cart/*` and an
 *   in-memory BehaviorSubject mirrors server state. Cart is global across
 *   all albums the user can access and survives browser/device.
 *
 * - When unauthenticated (access-code flow), state is per-code in
 *   localStorage. Cross-tab `storage` events keep tabs in sync.
 *
 * Drawer open/close state is exposed via cartDrawerOpen$ so the global
 * cart button (in the navbar) and the global drawer (in BaseLayoutComponent)
 * can broker visibility through this single service. (Folded into CartService
 * rather than a separate CartUiService — the surface is small enough that a
 * dedicated service would be overkill.)
 */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly items$ = new BehaviorSubject<CartItem[]>([]);
  private readonly drawerOpen$ = new BehaviorSubject<boolean>(false);
  private readonly errors$ = new Subject<string>();
  private readonly skipped$ = new Subject<string[]>();

  /** Anonymous-mode access code (null when authenticated). */
  private currentCode: string | null = null;
  /** True after loadForUser has fired at least once for the current session. */
  private userLoaded = false;

  constructor() {
    // Cross-tab sync for the anonymous (localStorage) path. Authed users hit
    // the server directly so cross-tab consistency comes from the next GET.
    if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
      window.addEventListener('storage', (e: StorageEvent) => {
        if (this.auth.isAuthenticatedSync()) return;
        if (!this.currentCode) return;
        if (e.key === this.storageKey(this.currentCode)) {
          this.loadForCode(this.currentCode);
        }
      });
    }
  }

  // ---------------------------------------------------------------------------
  // Streams
  // ---------------------------------------------------------------------------

  /** Emits the current cart state. */
  get cart$(): Observable<CartItem[]> { return this.items$.asObservable(); }

  /** Alias matching the spec naming. */
  get cartItems$(): Observable<CartItem[]> { return this.items$.asObservable(); }

  /** Drawer open/close state. */
  get cartDrawerOpen$(): Observable<boolean> { return this.drawerOpen$.asObservable(); }

  /** Surface non-fatal cart errors (e.g. cap toast text) to drawer. */
  get errors(): Observable<string> { return this.errors$.asObservable(); }

  /** Emits the X-Skipped-Photo-Ids list after a download with skipped items. */
  get skippedPhotoIds$(): Observable<string[]> { return this.skipped$.asObservable(); }

  /** Synchronous snapshot of cart items. */
  get items(): CartItem[] { return [...this.items$.value]; }

  /** Count of distinct items in the cart. */
  get count(): number { return this.items$.value.length; }

  // ---------------------------------------------------------------------------
  // Drawer state
  // ---------------------------------------------------------------------------

  openDrawer(): void { this.drawerOpen$.next(true); }
  closeDrawer(): void { this.drawerOpen$.next(false); }
  toggleDrawer(): void { this.drawerOpen$.next(!this.drawerOpen$.value); }

  // ---------------------------------------------------------------------------
  // Load
  // ---------------------------------------------------------------------------

  /** Hydrate the anonymous cart for a specific access code from localStorage. */
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
        const migrated = parsed
          .map((v: unknown) => this.migrateItem(v))
          .filter((v): v is CartItem => v !== null);
        this.items$.next(migrated);
        if (migrated.length !== parsed.length || this.didMigrate(parsed, migrated)) {
          this.persist();
        }
      } else {
        this.items$.next([]);
      }
    } catch {
      this.items$.next([]);
    }
  }

  /**
   * Hydrate the cart from /api/cart for the authenticated user. On the first
   * call after sign-in this also runs a one-time migration of any per-code
   * localStorage carts into the server cart.
   */
  async loadForUser(): Promise<void> {
    if (!this.auth.isAuthenticatedSync()) return;
    this.currentCode = null;

    if (!this.userLoaded) {
      this.userLoaded = true;
      // One-time migration: localStorage access-code cart → server cart on first authenticated load
      await this.migrateLocalStorageCarts();
    }

    const url = `${environment.apiUrl}/api/cart`;
    try {
      const resp = await firstValueFrom(this.http.get<CartResponse>(url));
      this.items$.next(this.normalizeServerItems(resp?.items ?? []));
    } catch (err) {
      console.error('CartService.loadForUser failed', err);
    }
  }

  // ---------------------------------------------------------------------------
  // Mutations
  // ---------------------------------------------------------------------------

  /**
   * Add a photo to the cart. On the authed path this POSTs to /api/cart;
   * the local items$ is updated optimistically on success. On the anonymous
   * path this is the existing localStorage path.
   *
   * Returns true if added, false on duplicate / local cap.
   */
  addItem(item: CartItem): boolean {
    if (this.auth.isAuthenticatedSync()) {
      this.addItemRemote(item);
      // Optimistic answer: drawer/badge updates happen via items$ in the HTTP
      // callback. Returning true matches the anonymous contract — callers that
      // care about precise outcomes should subscribe to errors$.
      return true;
    }
    return this.addItemLocal(item);
  }

  /**
   * Bulk add. Authed path: POSTs each item sequentially (server enforces cap).
   * Anonymous path: existing transactional addItems.
   */
  addItems(items: CartItem[]): number {
    if (!items || items.length === 0) return 0;
    if (this.auth.isAuthenticatedSync()) {
      for (const it of items) this.addItemRemote(it);
      return items.length; // best-effort; precise count surfaces via cart$
    }
    return this.addItemsLocal(items);
  }

  /** Remove an item by (photoId, quality). */
  removeItem(photoId: string, quality: CartQuality): void {
    if (this.auth.isAuthenticatedSync()) {
      const url = `${environment.apiUrl}/api/cart/${photoId}/${quality}`;
      this.http.delete(url).subscribe({
        next: () => {
          const updated = this.items$.value.filter(
            i => !(i.photoId === photoId && i.quality === quality)
          );
          this.items$.next(updated);
        },
        error: (err) => console.error('CartService.removeItem failed', err)
      });
      return;
    }
    const updated = this.items$.value.filter(
      i => !(i.photoId === photoId && i.quality === quality)
    );
    this.items$.next(updated);
    this.persist();
  }

  /** Update the quality of an existing item (anonymous path only — authed
   *  callers should remove + add). */
  updateQuality(photoId: string, oldQuality: CartQuality, newQuality: CartQuality): void {
    if (oldQuality === newQuality) return;
    if (this.auth.isAuthenticatedSync()) {
      // Authed path: implement as remove + add to keep server contract simple.
      const existing = this.items$.value.find(i => i.photoId === photoId && i.quality === oldQuality);
      this.removeItem(photoId, oldQuality);
      if (existing) {
        this.addItemRemote({ ...existing, quality: newQuality });
      }
      return;
    }
    const updated = this.items$.value.map(i =>
      (i.photoId === photoId && i.quality === oldQuality)
        ? { ...i, quality: newQuality }
        : i
    );
    this.items$.next(updated);
    this.persist();
  }

  /** Empty the cart. Authed: DELETE /api/cart. Anonymous: clear localStorage. */
  clear(): void {
    if (this.auth.isAuthenticatedSync()) {
      this.http.delete(`${environment.apiUrl}/api/cart`).subscribe({
        next: () => this.items$.next([]),
        error: (err) => console.error('CartService.clear failed', err)
      });
      return;
    }
    this.items$.next([]);
    this.persist();
  }

  /** True if a given (photoId, quality) is in the cart. */
  contains(photoId: string, quality: CartQuality): boolean {
    return this.items$.value.some(i => i.photoId === photoId && i.quality === quality);
  }

  // ---------------------------------------------------------------------------
  // Download (authed path only — anonymous path is in CartPanelComponent)
  // ---------------------------------------------------------------------------

  /**
   * Trigger the authenticated bulk-download flow.
   * Reads X-Skipped-Photo-Ids; on success clears the cart and emits any
   * skipped IDs via skippedPhotoIds$. Throws CartCapError on 409, else
   * a generic Error the caller can toast.
   */
  async download(): Promise<void> {
    if (!this.auth.isAuthenticatedSync()) {
      throw new Error('download() requires an authenticated session');
    }
    const url = `${environment.apiUrl}/api/cart/download`;
    try {
      const resp = await firstValueFrom(this.http.post(url, {}, {
        observe: 'response',
        responseType: 'blob'
      })) as HttpResponse<Blob>;

      const skippedHeader = resp.headers.get('X-Skipped-Photo-Ids') ?? '';
      const skipped = skippedHeader
        ? skippedHeader.split(',').map(s => s.trim()).filter(s => s.length > 0)
        : [];

      const blob = resp.body!;
      this.triggerBrowserDownload(blob, this.extractFilename(resp));

      // Clear cart and notify subscribers about skipped items.
      this.items$.next([]);
      this.skipped$.next(skipped);
    } catch (err) {
      if (err instanceof HttpErrorResponse) {
        if (err.status === 409) {
          let limit = MAX_CART_SIZE;
          // 409 body may arrive as Blob (because responseType=blob); try to parse.
          const body = err.error;
          if (body && typeof body === 'object' && 'limit' in body && typeof (body as { limit: unknown }).limit === 'number') {
            limit = (body as { limit: number }).limit;
          }
          throw new CartCapError(limit);
        }
      }
      throw err;
    }
  }

  // ---------------------------------------------------------------------------
  // Internals — auth path
  // ---------------------------------------------------------------------------

  private addItemRemote(item: CartItem): void {
    if (this.items$.value.length >= MAX_CART_SIZE) {
      this.errors$.next('Cart is full (100 items max). Please download or remove items first.');
      return;
    }
    const url = `${environment.apiUrl}/api/cart`;
    const body: { photoId: string; quality: CartQuality; sourceAlbumId?: string } = {
      photoId: item.photoId,
      quality: item.quality
    };
    if (item.sourceAlbumId) body.sourceAlbumId = item.sourceAlbumId;

    this.http.post<CartItem | null>(url, body).subscribe({
      next: (returned) => {
        // Avoid optimistic duplicate.
        const existing = this.items$.value;
        if (existing.some(i => i.photoId === item.photoId && i.quality === item.quality)) return;
        const merged: CartItem = returned
          ? this.normalizeServerItem({ ...item, ...returned })
          : { ...item };
        this.items$.next([...existing, merged]);
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 409) {
          const body = err.error;
          const limit = (body && typeof body === 'object' && 'limit' in body && typeof (body as { limit: unknown }).limit === 'number')
            ? (body as { limit: number }).limit
            : MAX_CART_SIZE;
          this.errors$.next(`Cart is full (${limit} items max). Please download or remove items first.`);
          return;
        }
        if (err.status === 403) {
          this.errors$.next('You no longer have access to this photo.');
          return;
        }
        console.error('CartService.addItemRemote failed', err);
        this.errors$.next('Failed to add to cart. Please try again.');
      }
    });
  }

  private async migrateLocalStorageCarts(): Promise<void> {
    if (typeof localStorage === 'undefined') return;
    const keys: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(`${STORAGE_KEY_PREFIX}-`)) keys.push(k);
    }
    if (keys.length === 0) return;

    for (const key of keys) {
      let parsed: unknown;
      try {
        const raw = localStorage.getItem(key);
        if (!raw) { localStorage.removeItem(key); continue; }
        parsed = JSON.parse(raw);
      } catch {
        localStorage.removeItem(key);
        continue;
      }
      if (!Array.isArray(parsed)) {
        localStorage.removeItem(key);
        continue;
      }
      for (const v of parsed) {
        const item = this.migrateItem(v);
        if (!item) continue;
        try {
          await firstValueFrom(this.http.post(`${environment.apiUrl}/api/cart`, {
            photoId: item.photoId,
            quality: item.quality
          }));
        } catch (err) {
          if (err instanceof HttpErrorResponse && (err.status === 403 || err.status === 409)) {
            // Skip silently on 403 (no access) or 409 (cap reached).
            continue;
          }
          console.warn('CartService migration: failed to migrate item', item.photoId, err);
        }
      }
      localStorage.removeItem(key);
    }
  }

  // ---------------------------------------------------------------------------
  // Internals — anonymous path
  // ---------------------------------------------------------------------------

  private addItemLocal(item: CartItem): boolean {
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

  private addItemsLocal(items: CartItem[]): number {
    const existing = this.items$.value;
    const seen = new Set<string>(existing.map(i => `${i.photoId}::${i.quality}`));
    const next = [...existing];
    let added = 0;
    for (const item of items) {
      if (next.length >= MAX_CART_SIZE) break;
      const key = `${item.photoId}::${item.quality}`;
      if (seen.has(key)) continue;
      seen.add(key);
      next.push(item);
      added++;
    }
    if (added > 0) {
      this.items$.next(next);
      this.persist();
    }
    return added;
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  private storageKey(code: string): string {
    return `${STORAGE_KEY_PREFIX}-${code}`;
  }

  private persist(): void {
    if (this.auth.isAuthenticatedSync()) return;
    if (!this.currentCode) return;
    try {
      localStorage.setItem(
        this.storageKey(this.currentCode),
        JSON.stringify(this.items$.value)
      );
    } catch {
      // localStorage may be full or disabled — ignore.
    }
  }

  /**
   * Returns a normalised <see cref="CartItem"/> for a value loaded from localStorage,
   * or <c>null</c> if the value is unsalvageable (missing photoId / fileName).
   */
  private migrateItem(value: unknown): CartItem | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    if (typeof v['photoId'] !== 'string' || typeof v['fileName'] !== 'string') return null;
    const rawQuality = v['quality'];
    const quality: CartQuality = (typeof rawQuality === 'string' && (VALID_QUALITIES as ReadonlyArray<string>).includes(rawQuality))
      ? rawQuality as CartQuality
      : DEFAULT_CART_QUALITY;
    return {
      photoId: v['photoId'] as string,
      fileName: v['fileName'] as string,
      thumbnailUrl: typeof v['thumbnailUrl'] === 'string' ? v['thumbnailUrl'] as string : undefined,
      quality,
      sourceAlbumId: typeof v['sourceAlbumId'] === 'string' ? v['sourceAlbumId'] as string : undefined,
      sourceAlbumTitle: typeof v['sourceAlbumTitle'] === 'string' ? v['sourceAlbumTitle'] as string : undefined,
      addedAt: typeof v['addedAt'] === 'string' ? v['addedAt'] as string : undefined
    };
  }

  private didMigrate(parsed: unknown[], migrated: CartItem[]): boolean {
    if (parsed.length !== migrated.length) return true;
    for (let i = 0; i < parsed.length; i++) {
      const p = parsed[i] as Record<string, unknown> | null;
      if (!p || p['quality'] !== migrated[i].quality) return true;
    }
    return false;
  }

  private normalizeServerItem(item: CartItem): CartItem {
    const quality: CartQuality = (VALID_QUALITIES as ReadonlyArray<string>).includes(item.quality)
      ? item.quality
      : DEFAULT_CART_QUALITY;
    return { ...item, quality };
  }

  private normalizeServerItems(items: CartItem[]): CartItem[] {
    return items.map(i => this.normalizeServerItem(i));
  }

  private triggerBrowserDownload(blob: Blob, filename: string | null): void {
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = filename ?? `photos-${Date.now()}.zip`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(objectUrl);
  }

  private extractFilename(response: HttpResponse<Blob>): string | null {
    const dispo = response.headers.get('Content-Disposition');
    if (!dispo) return null;
    const match = /filename="?([^"]+)"?/i.exec(dispo);
    return match ? match[1] : null;
  }
}
