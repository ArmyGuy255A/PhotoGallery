import { signal, computed, Signal } from '@angular/core';
import { Observable, Subject, Subscription } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

/**
 * Generic page envelope returned by the paginated photo endpoints
 * (`GET /api/albums/{id}/photos` and `GET /api/code/{code}/photos`).
 *
 * Items are typed by the caller so the loader can be reused across the
 * authenticated album view (PhotoListDto) and the public code-gallery view
 * (PublicPhotoListDto).
 */
export interface PhotoPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
}

/**
 * Function the loader calls for each page. The component passes a closure that
 * fans out to the right HTTP endpoint — the loader has no opinion about the URL
 * or the auth posture, only about pagination state.
 */
export type PhotoPageFetcher<T> = (page: number, pageSize: number) => Observable<PhotoPage<T>>;

/**
 * Progressive photo-grid loader (Phase 6).
 *
 * Wraps the paged photo endpoints in a single stateful object the component
 * can render directly. Exposes signals for the photos accumulated so far, the
 * loading flag (drives the skeleton grid), and `hasMore` (drives the
 * IntersectionObserver sentinel + the empty-state copy).
 *
 * The component owns wiring concerns — observer setup, route-param changes —
 * but the loader handles the rules that are easy to get wrong: never issue two
 * concurrent fetches, never advance `page` past a server that says
 * `hasMore=false`, never emit the "empty" state until a real page has come
 * back claiming no items.
 */
export class PhotoPageLoader<T> {
  /** Photos accumulated across all loaded pages. */
  readonly photos = signal<T[]>([]);

  /** Highest page number successfully loaded. 0 before the first fetch. */
  readonly page = signal<number>(0);

  /**
   * Whether the server still has more pages to serve. Starts true so the
   * sentinel can fire once on mount; flipped to false by the first response
   * whose `hasMore` is false.
   */
  readonly hasMore = signal<boolean>(true);

  /** True while a fetch is in flight. */
  readonly isLoading = signal<boolean>(false);

  /** Latest totalCount reported by the server. */
  readonly totalCount = signal<number>(0);

  /**
   * Flipped to true the first time <c>loadNext</c> completes successfully
   * (including an empty envelope). Stays false on errors, so a transient
   * failure doesn't force the component into the "X of Y" banner without
   * a real page result behind it.
   *
   * Drives the album header UX:
   *   - false + isLoading → centred "Loading photos…" spinner
   *   - true  + photos<total → inline "Loaded X of Y photos…" banner
   *   - true  + photos===total → grid only
   */
  readonly hasLoadedFirstPage = signal<boolean>(false);

  /**
   * Optional hook fired after every successful page load (after
   * <c>onPageLoaded</c>). Components use this to nudge an
   * IntersectionObserver into re-evaluating the sentinel — once the new page
   * is appended the sentinel often remains within the viewport (especially
   * with a generous <c>rootMargin</c>), and IntersectionObserver only fires on
   * intersection-state transitions, so "still visible" by itself never
   * triggers another load. The conventional fix is for the consumer to
   * <c>unobserve(sentinel); observe(sentinel);</c> which forces a fresh
   * evaluation against the now-shorter page.
   *
   * Plain mutable property rather than a constructor arg so the wiring can be
   * attached *after* the observer is created in <c>ngAfterViewInit</c>.
   */
  onLoadCompleted?: () => void;

  /**
   * Convenience signal: true exactly when the loader has finished loading,
   * accumulated zero photos, and the server confirmed there are no more pages.
   * This is the trigger for the "No photos yet" empty-state copy — never just
   * <c>photos.length === 0</c>, which would also fire during slow loads.
   */
  readonly isEmpty: Signal<boolean> = computed(
    () => !this.isLoading() && this.photos().length === 0 && !this.hasMore()
  );

  private readonly destroy$ = new Subject<void>();
  private inflight: Subscription | null = null;

  constructor(
    private readonly fetcher: PhotoPageFetcher<T>,
    /** Server default; callers can override per album / code. */
    private readonly pageSize: number = 20,
    /**
     * Optional callback invoked after each successful page load with the
     * just-appended items. Useful for components that need to do per-photo
     * setup (e.g. seeding a per-photo quality dropdown) without having to
     * subscribe to the photos signal manually.
     */
    private readonly onPageLoaded?: (newItems: T[]) => void
  ) {}

  /**
   * Load the next page. No-ops when a fetch is already in flight or the server
   * has indicated <c>hasMore=false</c> — the sentinel can fire repeatedly during
   * a slow connection and we want each loadNext call after the last page to be
   * a cheap noop, not a duplicate request.
   */
  loadNext(): void {
    if (this.isLoading() || !this.hasMore()) {
      return;
    }
    const nextPage = this.page() + 1;
    this.isLoading.set(true);

    this.inflight = this.fetcher(nextPage, this.pageSize)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (envelope) => {
          const items = envelope?.items ?? [];
          this.photos.update(existing => [...existing, ...items]);
          this.page.set(envelope?.page ?? nextPage);
          this.totalCount.set(envelope?.totalCount ?? this.photos().length);
          this.hasMore.set(!!envelope?.hasMore);
          this.isLoading.set(false);
          this.hasLoadedFirstPage.set(true);
          this.onPageLoaded?.(items);
          this.onLoadCompleted?.();

          // Auto-trickle: schedule the next page so all photos eventually
          // land without needing the user to scroll or open a carousel
          // beyond the loaded range. Bounded by hasMore so the recursion
          // terminates on the server's last page.
          if (this.autoLoadEnabled && this.hasMore()) {
            setTimeout(() => this.loadNext(), this.autoLoadDelayMs);
          }
        },
        error: () => {
          // Surface the failure as "not loading any more" without flipping
          // hasMore — a future retry (e.g. user re-scrolls) can re-attempt.
          this.isLoading.set(false);
        }
      });
  }

  /**
   * Reset state and load the first page. Used when the route param changes
   * (e.g. user navigates to a different album).
   */
  reset(): void {
    this.inflight?.unsubscribe();
    this.inflight = null;
    this.photos.set([]);
    this.page.set(0);
    this.hasMore.set(true);
    this.totalCount.set(0);
    this.isLoading.set(false);
    this.hasLoadedFirstPage.set(false);
  }

  /**
   * Remove a single photo from the cached list (used after a per-photo
   * delete). Does NOT re-fetch — the next loadNext call will continue from the
   * server's perspective of the page boundary, which is acceptable: the local
   * gap closes the next time the user navigates back.
   */
  removeWhere(predicate: (photo: T) => boolean): void {
    this.photos.update(existing => existing.filter(p => !predicate(p)));
  }

  /**
   * In-place refresh of the pages already loaded. Used during active uploads:
   * the album-activity poll fires every few seconds and previously called
   * <c>reset() + enableAutoLoad()</c>, which dumped the entire photo array
   * and re-rendered from page 1 — that's what caused the scroll glitch
   * users see on a long album during a bulk upload.
   *
   * Behaviour:
   *  - Re-fetches pages 1..currentPage in parallel.
   *  - Merges items into the existing array <b>by id</b>: an existing item
   *    whose payload hasn't changed retains its array slot and object
   *    reference (Angular's *ngFor trackBy keeps the DOM node), and only
   *    items whose fields actually changed get a new object.
   *  - If the server says <c>totalCount</c> grew, leaves <c>hasMore=true</c>
   *    so the auto-load loop continues from where it left off; otherwise
   *    preserves the current <c>hasMore</c>.
   *  - Never resets the photos array to empty — even on a fetch error.
   *
   * Caller must provide a key extractor so the loader can match items by id
   * (the loader itself is generic and doesn't know that <c>T</c> has an .id).
   */
  refreshLoaded(keyOf: (item: T) => string): void {
    const loadedPage = this.page();
    if (loadedPage === 0) {
      this.loadNext();
      return;
    }
    if (this.isLoading()) return;
    this.isLoading.set(true);

    // Fetch pages 1..loadedPage sequentially so we observe the same FileName
    // ordering the user already sees on screen.
    const fetchPage = (p: number): Promise<PhotoPage<T>> => new Promise((resolve, reject) => {
      const sub = this.fetcher(p, this.pageSize).subscribe({
        next: env => { sub.unsubscribe(); resolve(env); },
        error: err => { sub.unsubscribe(); reject(err); }
      });
    });

    (async () => {
      const merged: T[] = [];
      let lastEnvelope: PhotoPage<T> | null = null;
      try {
        for (let p = 1; p <= loadedPage; p++) {
          const env = await fetchPage(p);
          lastEnvelope = env;
          for (const item of env?.items ?? []) merged.push(item);
        }
      } catch {
        this.isLoading.set(false);
        return;
      }

      // Merge by id, preserving object reference where the fields haven't
      // changed. This is what keeps Angular's *ngFor from blowing away
      // every DOM node on each poll cycle.
      const existing = this.photos();
      const existingById = new Map(existing.map(p => [keyOf(p), p] as const));
      const next = merged.map(incoming => {
        const id = keyOf(incoming);
        const prior = existingById.get(id);
        if (prior && this.shallowEqualPhoto(prior, incoming)) return prior;
        return incoming;
      });
      this.photos.set(next);
      if (lastEnvelope) {
        this.totalCount.set(lastEnvelope.totalCount ?? next.length);
        // If the server has new pages beyond what we've loaded, keep
        // auto-load going from where we left off.
        const moreAfterLoaded = lastEnvelope.totalCount > next.length;
        this.hasMore.set(moreAfterLoaded || !!lastEnvelope.hasMore);
        if (moreAfterLoaded && this.autoLoadEnabled) {
          setTimeout(() => this.loadNext(), this.autoLoadDelayMs);
        }
      }
      this.isLoading.set(false);
    })();
  }

  /**
   * Shallow field-by-field comparison used by <see cref="refreshLoaded"/> to
   * decide whether an incoming item is "the same" as the cached one (so the
   * old object reference can be reused, preserving the trackBy identity).
   * Works for plain-data photo DTOs; doesn't care about nested objects.
   */
  private shallowEqualPhoto(a: T, b: T): boolean {
    const ak = Object.keys(a as Record<string, unknown>);
    const bk = Object.keys(b as Record<string, unknown>);
    if (ak.length !== bk.length) return false;
    for (const k of ak) {
      if ((a as Record<string, unknown>)[k] !== (b as Record<string, unknown>)[k]) return false;
    }
    return true;
  }

  /** Tear down on component destroy. */
  destroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Auto-trickle mode: after each successful page load the loader schedules
   * the next <c>loadNext</c> call automatically until the server reports
   * <c>hasMore=false</c>. The small inter-page delay keeps the API call rate
   * reasonable while the user is also interacting with the grid, and
   * spaces out the page renders so the browser doesn't paint a 500-image
   * grid in a single tick.
   *
   * Components that previously relied on the IntersectionObserver-driven
   * pagination still work — auto-load is additive. The "Loaded X of Y"
   * banner becomes unnecessary because every photo eventually appears on
   * its own; components are free to drop it.
   */
  enableAutoLoad(delayMs: number = 200): void {
    this.autoLoadEnabled = true;
    this.autoLoadDelayMs = delayMs;
    if (!this.isLoading() && this.hasMore()) {
      this.loadNext();
    }
  }

  private autoLoadEnabled = false;
  private autoLoadDelayMs = 200;
}
