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

  /** Tear down on component destroy. */
  destroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
