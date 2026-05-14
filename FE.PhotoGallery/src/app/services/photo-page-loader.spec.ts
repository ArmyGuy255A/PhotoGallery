import { Subject, of, throwError } from 'rxjs';
import { PhotoPage, PhotoPageLoader } from './photo-page-loader';

interface FakePhoto { id: string; fileName: string; }

function envelope(items: FakePhoto[], page: number, pageSize: number, totalCount: number, hasMore: boolean): PhotoPage<FakePhoto> {
  return { items, page, pageSize, totalCount, hasMore };
}

describe('PhotoPageLoader', () => {
  it('starts empty, with page=0, hasMore=true, isLoading=false', () => {
    const loader = new PhotoPageLoader<FakePhoto>(() => of(envelope([], 1, 20, 0, false)));
    expect(loader.photos().length).toBe(0);
    expect(loader.page()).toBe(0);
    expect(loader.hasMore()).toBeTrue();
    expect(loader.isLoading()).toBeFalse();
    expect(loader.isEmpty()).toBeFalse(); // not loaded yet
  });

  it('loadNext fetches page 1, appends, and tracks hasMore', () => {
    const fetcher = jasmine.createSpy('fetcher').and.returnValue(
      of(envelope([{ id: 'p1', fileName: 'a.jpg' }, { id: 'p2', fileName: 'b.jpg' }], 1, 20, 25, true))
    );
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    loader.loadNext();

    expect(fetcher).toHaveBeenCalledOnceWith(1, 20);
    expect(loader.photos().map(p => p.id)).toEqual(['p1', 'p2']);
    expect(loader.page()).toBe(1);
    expect(loader.totalCount()).toBe(25);
    expect(loader.hasMore()).toBeTrue();
    expect(loader.isLoading()).toBeFalse();
  });

  it('subsequent loadNext appends page 2 results without losing page 1', () => {
    const responses: PhotoPage<FakePhoto>[] = [
      envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 1, 3, true),
      envelope([{ id: 'p2', fileName: 'b.jpg' }], 2, 1, 3, true),
      envelope([{ id: 'p3', fileName: 'c.jpg' }], 3, 1, 3, false)
    ];
    const fetcher = jasmine.createSpy('fetcher').and.callFake((page: number) => of(responses[page - 1]));
    const loader = new PhotoPageLoader<FakePhoto>(fetcher, 1);

    loader.loadNext(); // page 1
    loader.loadNext(); // page 2
    loader.loadNext(); // page 3 — last

    expect(loader.photos().map(p => p.id)).toEqual(['p1', 'p2', 'p3']);
    expect(loader.page()).toBe(3);
    expect(loader.hasMore()).toBeFalse();
    expect(loader.isEmpty()).toBeFalse(); // photos populated
  });

  it('loadNext is a noop while a fetch is in flight (no duplicate requests)', () => {
    const subject = new Subject<PhotoPage<FakePhoto>>();
    const fetcher = jasmine.createSpy('fetcher').and.returnValue(subject.asObservable());
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    loader.loadNext();
    expect(loader.isLoading()).toBeTrue();
    expect(fetcher).toHaveBeenCalledTimes(1);

    loader.loadNext();
    loader.loadNext();
    expect(fetcher).toHaveBeenCalledTimes(1);

    subject.next(envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 20, 1, false));
    subject.complete();

    expect(loader.isLoading()).toBeFalse();
    expect(loader.photos().length).toBe(1);
  });

  it('loadNext is a noop once hasMore is false', () => {
    const fetcher = jasmine.createSpy('fetcher').and.returnValue(
      of(envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 20, 1, false))
    );
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    loader.loadNext();
    loader.loadNext();
    loader.loadNext();

    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it('isEmpty reports true only after the server confirms no items + no more pages', () => {
    const fetcher = () => of(envelope([], 1, 20, 0, false));
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    expect(loader.isEmpty()).toBeFalse(); // hasMore still true, never loaded

    loader.loadNext();

    expect(loader.isLoading()).toBeFalse();
    expect(loader.photos().length).toBe(0);
    expect(loader.hasMore()).toBeFalse();
    expect(loader.isEmpty()).toBeTrue();
  });

  it('isEmpty stays false during a slow load even when photos.length === 0 (no flash of empty-state)', () => {
    const subject = new Subject<PhotoPage<FakePhoto>>();
    const loader = new PhotoPageLoader<FakePhoto>(() => subject.asObservable());

    loader.loadNext();

    // While loading, isEmpty must be false even though photos.length === 0.
    expect(loader.isLoading()).toBeTrue();
    expect(loader.photos().length).toBe(0);
    expect(loader.isEmpty()).toBeFalse();
  });

  it('reset clears state so the loader can be reused for a different album', () => {
    const fetcher = jasmine.createSpy('fetcher').and.returnValue(
      of(envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 20, 1, false))
    );
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    loader.loadNext();
    expect(loader.photos().length).toBe(1);

    loader.reset();
    expect(loader.photos().length).toBe(0);
    expect(loader.page()).toBe(0);
    expect(loader.hasMore()).toBeTrue();
    expect(loader.isLoading()).toBeFalse();
  });

  it('on error keeps hasMore=true and clears isLoading so a retry is possible', () => {
    const fetcher = jasmine.createSpy('fetcher').and.returnValue(throwError(() => new Error('boom')));
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);

    loader.loadNext();

    expect(loader.isLoading()).toBeFalse();
    expect(loader.hasMore()).toBeTrue();
    expect(loader.photos().length).toBe(0);
  });

  it('removeWhere strips matching photos from the cached list', () => {
    const fetcher = () => of(envelope(
      [
        { id: 'p1', fileName: 'a.jpg' },
        { id: 'p2', fileName: 'b.jpg' },
        { id: 'p3', fileName: 'c.jpg' }
      ], 1, 20, 3, false));
    const loader = new PhotoPageLoader<FakePhoto>(fetcher);
    loader.loadNext();

    loader.removeWhere(p => p.id === 'p2');

    expect(loader.photos().map(p => p.id)).toEqual(['p1', 'p3']);
  });

  it('destroy unsubscribes in-flight requests', () => {
    const subject = new Subject<PhotoPage<FakePhoto>>();
    const loader = new PhotoPageLoader<FakePhoto>(() => subject.asObservable());

    loader.loadNext();
    expect(loader.isLoading()).toBeTrue();

    loader.destroy();
    subject.next(envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 20, 1, false));

    // Since the subscription was torn down, the photos array should NOT have been mutated.
    expect(loader.photos().length).toBe(0);
  });

  // -----------------------------------------------------------------------
  // Phase 6 progressive-load bug fix: onLoadCompleted hook lets a component
  // nudge its IntersectionObserver to re-evaluate after each page arrives.
  // Without it the sentinel can stay "intersecting" silently after page N
  // appends and the observer (which only fires on transitions) never asks
  // for page N+1.
  // -----------------------------------------------------------------------
  it('invokes onLoadCompleted after each successful page load', () => {
    const responses: PhotoPage<FakePhoto>[] = [
      envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 1, 2, true),
      envelope([{ id: 'p2', fileName: 'b.jpg' }], 2, 1, 2, false)
    ];
    const fetcher = (page: number) => of(responses[page - 1]);
    const loader = new PhotoPageLoader<FakePhoto>(fetcher, 1);
    const completed = jasmine.createSpy('onLoadCompleted');
    loader.onLoadCompleted = completed;

    loader.loadNext();
    loader.loadNext();

    expect(completed).toHaveBeenCalledTimes(2);
  });

  it('multiple sequential loadNext calls grow the photos array beyond a single page', () => {
    // Regression test for the progressive-load bug: simulate what the
    // IntersectionObserver fix is supposed to enable — once page 1 lands and
    // the observer re-evaluates the still-intersecting sentinel, page 2
    // should load, then page 3, until hasMore=false.
    const responses: PhotoPage<FakePhoto>[] = [
      envelope([{ id: 'p1', fileName: 'a.jpg' }], 1, 1, 3, true),
      envelope([{ id: 'p2', fileName: 'b.jpg' }], 2, 1, 3, true),
      envelope([{ id: 'p3', fileName: 'c.jpg' }], 3, 1, 3, false)
    ];
    const fetcher = (page: number) => of(responses[page - 1]);
    const loader = new PhotoPageLoader<FakePhoto>(fetcher, 1);

    // Simulate the component wiring: re-trigger loadNext from
    // onLoadCompleted whenever the observer would still be intersecting.
    // hasMore guards prevent infinite recursion when the server says we're
    // done.
    loader.onLoadCompleted = () => {
      if (loader.hasMore()) {
        loader.loadNext();
      }
    };

    loader.loadNext();

    expect(loader.photos().length).toBe(3);
    expect(loader.hasMore()).toBeFalse();
  });
});
