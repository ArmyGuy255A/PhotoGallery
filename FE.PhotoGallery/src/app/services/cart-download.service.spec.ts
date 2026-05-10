import { TestBed } from '@angular/core/testing';
import { firstValueFrom, lastValueFrom, toArray, take } from 'rxjs';
import { CartDownloadProgress, CartDownloadService } from './cart-download.service';
import { CartItem } from './cart.service';

/**
 * Specs for {@link CartDownloadService}.
 *
 * Strategy: stub `fetch` to return a fake manifest then a series of
 * fake blob streams. Stub `showSaveFilePicker` away so we exercise
 * the blob-fallback path (Karma + ChromeHeadless does expose
 * `showSaveFilePicker`, but only behind user activation, so we
 * delete it to avoid a permission prompt).
 *
 * Coverage:
 * - Manifest fetched first; each item URL fetched after
 * - Progress sequence is correct
 * - Fetch failures collected into the partial-failure list
 * - AbortController fires on unsubscribe
 */
describe('CartDownloadService', () => {
  let service: CartDownloadService;
  let originalFetch: typeof fetch;
  let originalSavePicker: unknown;
  let originalCreateObjectUrl: typeof URL.createObjectURL;
  let originalRevokeObjectUrl: typeof URL.revokeObjectURL;
  let createdLinks: HTMLAnchorElement[];

  const code = 'TESTCODE';
  const items: CartItem[] = [
    { photoId: 'p1', fileName: 'IMG_0001.jpg', quality: 'Medium' },
    { photoId: 'p2', fileName: 'IMG_0002.jpg', quality: 'High' }
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CartDownloadService);

    originalFetch = window.fetch;
    originalSavePicker = (window as unknown as Record<string, unknown>)['showSaveFilePicker'];
    delete (window as unknown as Record<string, unknown>)['showSaveFilePicker'];

    originalCreateObjectUrl = URL.createObjectURL;
    originalRevokeObjectUrl = URL.revokeObjectURL;
    URL.createObjectURL = () => 'blob:fake-url';
    URL.revokeObjectURL = () => undefined;

    createdLinks = [];
    const realCreateElement = document.createElement.bind(document);
    spyOn(document, 'createElement').and.callFake((tag: string) => {
      const el = realCreateElement(tag);
      if (tag === 'a') {
        spyOn(el, 'click').and.stub();
        createdLinks.push(el as HTMLAnchorElement);
      }
      return el;
    });
  });

  afterEach(() => {
    window.fetch = originalFetch;
    if (originalSavePicker !== undefined) {
      (window as unknown as Record<string, unknown>)['showSaveFilePicker'] = originalSavePicker;
    }
    URL.createObjectURL = originalCreateObjectUrl;
    URL.revokeObjectURL = originalRevokeObjectUrl;
  });

  /** Build a Response that streams a single Uint8Array chunk. */
  function streamResponse(bytes: Uint8Array): Response {
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes);
        controller.close();
      }
    });
    return new Response(stream, { status: 200, headers: { 'Content-Type': 'image/jpeg' } });
  }

  function manifestResponse(): Response {
    return new Response(JSON.stringify({
      albumTitle: 'Wedding 2026',
      fileNamePrefix: 'Wedding_2026-20260509',
      items: items.map(i => ({
        photoId: i.photoId,
        quality: i.quality,
        fileName: `${i.quality}/${i.fileName}`,
        url: `https://minio.test/photogallery/${i.photoId}.jpg?sig=fake`
      }))
    }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }

  it('fetches the manifest first, then each item URL in order', async () => {
    const calls: string[] = [];
    window.fetch = (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      calls.push(url);
      if (url.includes('/cart/manifest')) {
        return Promise.resolve(manifestResponse());
      }
      return Promise.resolve(streamResponse(new Uint8Array([1, 2, 3, 4])));
    };

    await lastValueFrom(service.downloadCart(code, items));

    expect(calls.length).toBe(3);
    expect(calls[0]).toContain('/cart/manifest');
    expect(calls[1]).toContain('p1');
    expect(calls[2]).toContain('p2');
  });

  it('emits the progress sequence manifest -> downloading*N -> zipping -> saving -> done', async () => {
    window.fetch = (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/cart/manifest')) return Promise.resolve(manifestResponse());
      return Promise.resolve(streamResponse(new Uint8Array([9, 9, 9])));
    };

    const events = await lastValueFrom(
      service.downloadCart(code, items).pipe(toArray())
    ) as CartDownloadProgress[];

    const phases = events.map(e => e.phase);
    expect(phases[0]).toBe('manifest');
    expect(phases.filter(p => p === 'downloading').length).toBe(3); // initial 0/2 + 1/2 + 2/2
    expect(phases).toContain('zipping');
    expect(phases).toContain('saving');
    expect(phases[phases.length - 1]).toBe('done');

    const last = events[events.length - 1];
    if (last.phase !== 'done') throw new Error('expected done');
    expect(last.completed).toBe(2);
    expect(last.total).toBe(2);
    expect(last.failed.length).toBe(0);

    // blob-fallback delivery path must have been invoked
    expect(createdLinks.length).toBe(1);
    expect(createdLinks[0].download).toBe('Wedding_2026-20260509.zip');
  });

  it('collects fetch failures into the partial-failure list', async () => {
    window.fetch = (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/cart/manifest')) return Promise.resolve(manifestResponse());
      if (url.includes('p1')) return Promise.resolve(streamResponse(new Uint8Array([0])));
      return Promise.resolve(new Response('not found', { status: 404 }));
    };

    const events = await lastValueFrom(
      service.downloadCart(code, items).pipe(toArray())
    ) as CartDownloadProgress[];

    const last = events[events.length - 1];
    if (last.phase !== 'done') throw new Error('expected done');
    expect(last.completed).toBe(1);
    expect(last.total).toBe(2);
    expect(last.failed.length).toBe(1);
    expect(last.failed[0].fileName).toContain('IMG_0002.jpg');
    expect(last.failed[0].reason).toContain('404');
  });

  it('aborts the in-flight fetch when the subscription is torn down', async () => {
    const abortReasons: unknown[] = [];
    let manifestResolve: ((r: Response) => void) | null = null;

    window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
      init?.signal?.addEventListener('abort', () => abortReasons.push(init.signal?.reason ?? 'aborted'));
      return new Promise<Response>((resolve, reject) => {
        manifestResolve = resolve;
        init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
      });
    };

    const sub = service.downloadCart(code, items).subscribe();

    sub.unsubscribe();
    await new Promise(r => setTimeout(r, 0));

    expect(abortReasons.length).toBeGreaterThanOrEqual(1);
    // Resolving after unsubscribe should be a no-op (consumer is gone).
    if (manifestResolve) {
      (manifestResolve as (r: Response) => void)(manifestResponse());
    }
  });

  it('emits error phase on manifest HTTP failure', async () => {
    window.fetch = () => Promise.resolve(new Response('forbidden', { status: 403 }));

    const events = await lastValueFrom(
      service.downloadCart(code, items).pipe(toArray())
    ) as CartDownloadProgress[];

    const last = events[events.length - 1];
    expect(last.phase).toBe('error');
    if (last.phase === 'error') {
      expect(last.message.length).toBeGreaterThan(0);
    }
  });

  it('emits done with empty totals when manifest comes back with zero items', async () => {
    window.fetch = (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/cart/manifest')) {
        return Promise.resolve(new Response(
          JSON.stringify({ albumTitle: 'A', fileNamePrefix: 'A-x', items: [] }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        ));
      }
      throw new Error('item URL should not be fetched');
    };

    const last = await firstValueFrom(
      service.downloadCart(code, []).pipe(take(2), toArray())
    ) as CartDownloadProgress[];
    const finalEvent = last[last.length - 1];
    expect(finalEvent.phase).toBe('done');
    if (finalEvent.phase === 'done') {
      expect(finalEvent.total).toBe(0);
      expect(finalEvent.completed).toBe(0);
    }
  });
});
