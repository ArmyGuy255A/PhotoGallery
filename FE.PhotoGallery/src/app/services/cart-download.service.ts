import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { downloadZip } from 'client-zip';
import { environment } from '../../environments/environment';
import { CartItem } from './cart.service';

/**
 * Phases the cart download flow walks through. Consumers render the UI
 * (progress bar, cancel button, partial-failure list) by switching on this.
 */
export type CartDownloadProgress =
  | { phase: 'manifest' }
  | { phase: 'downloading'; completed: number; total: number }
  | { phase: 'zipping' }
  | { phase: 'saving' }
  | { phase: 'done'; completed: number; total: number; failed: ReadonlyArray<{ fileName: string; reason: string }> }
  | { phase: 'error'; message: string };

/** Wire shape of <c>POST /api/code/{code}/cart/manifest</c> response. */
interface ManifestEntry {
  photoId: string;
  quality: string;
  fileName: string;
  url: string;
}

interface ManifestResponse {
  albumTitle: string;
  fileNamePrefix: string;
  items: ManifestEntry[];
}

/**
 * Wraps the backend manifest endpoint + client-zip + browser save flow.
 *
 * Two delivery paths:
 * - Chromium: <c>showSaveFilePicker</c> → <c>FileSystemWritableFileStream</c>
 *   so the ZIP is streamed directly to disk (no in-memory buffering, supports
 *   large carts on Chromium).
 * - Firefox / Safari: blob fallback. The full ZIP is buffered into a Blob,
 *   then offered via a synthetic <c>&lt;a download&gt;</c> click. Memory cost
 *   = sum of all item sizes; a 100 × 20 MB Original cart on Firefox could OOM.
 *   Future work may lower the cap for non-Chromium; keep the cap as-is for
 *   now per plan ("don't drop preemptively").
 *
 * Cancellation: <c>AbortController</c> aborts any in-flight fetch and the
 * file-picker writable. Subscribers cancel by unsubscribing — the inner
 * controller fires automatically.
 */
@Injectable({ providedIn: 'root' })
export class CartDownloadService {
  private readonly apiBase = environment.apiUrl || '';

  /** Detected once on first download and surfaced via console.info. */
  private static loggedDeliveryPath = false;

  /**
   * Run the full cart download. Emits a sequence of progress updates and
   * completes after <c>done</c> or <c>error</c>. Unsubscribing aborts.
   */
  downloadCart(code: string, items: ReadonlyArray<CartItem>): Observable<CartDownloadProgress> {
    return new Observable<CartDownloadProgress>(subscriber => {
      const controller = new AbortController();
      const signal = controller.signal;

      const run = async () => {
        try {
          subscriber.next({ phase: 'manifest' });

          const manifest = await this.fetchManifest(code, items, signal);
          if (!manifest.items.length) {
            subscriber.next({
              phase: 'done',
              completed: 0,
              total: 0,
              failed: []
            });
            subscriber.complete();
            return;
          }

          const total = manifest.items.length;
          subscriber.next({ phase: 'downloading', completed: 0, total });

          const failed: { fileName: string; reason: string }[] = [];
          const fetched: { name: string; input: ReadableStream<Uint8Array> | Blob; lastModified: Date }[] = [];

          let completed = 0;
          for (const entry of manifest.items) {
            if (signal.aborted) return;
            try {
              const resp = await fetch(entry.url, { signal });
              if (!resp.ok || !resp.body) {
                failed.push({ fileName: entry.fileName, reason: `HTTP ${resp.status}` });
              } else {
                fetched.push({
                  name: entry.fileName,
                  input: resp.body,
                  lastModified: new Date()
                });
              }
            } catch (err: unknown) {
              if (signal.aborted) return;
              const reason = err instanceof Error ? err.message : 'fetch failed';
              failed.push({ fileName: entry.fileName, reason });
            }
            completed++;
            subscriber.next({ phase: 'downloading', completed, total });
          }

          if (!fetched.length) {
            subscriber.next({
              phase: 'done',
              completed: 0,
              total,
              failed
            });
            subscriber.complete();
            return;
          }

          subscriber.next({ phase: 'zipping' });
          const zipResponse = downloadZip(fetched);
          const zipName = `${manifest.fileNamePrefix}.zip`;

          subscriber.next({ phase: 'saving' });
          await this.deliver(zipResponse, zipName, signal);

          subscriber.next({
            phase: 'done',
            completed: fetched.length,
            total,
            failed
          });
          subscriber.complete();
        } catch (err: unknown) {
          if (signal.aborted) {
            // Treat aborts as silent completion — UI clears the state itself.
            subscriber.complete();
            return;
          }
          const message = err instanceof Error ? err.message : 'Download failed';
          subscriber.next({ phase: 'error', message });
          subscriber.complete();
        }
      };

      run();

      return () => {
        if (!signal.aborted) controller.abort();
      };
    });
  }

  private async fetchManifest(
    code: string,
    items: ReadonlyArray<CartItem>,
    signal: AbortSignal
  ): Promise<ManifestResponse> {
    const url = `${this.apiBase}/api/code/${encodeURIComponent(code)}/cart/manifest`;
    const body = JSON.stringify({
      items: items.map(i => ({ photoId: i.photoId, quality: i.quality }))
    });
    const resp = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
      credentials: 'include',
      signal
    });
    if (!resp.ok) {
      const text = await resp.text().catch(() => '');
      throw new Error(text || `Manifest request failed (HTTP ${resp.status})`);
    }
    return (await resp.json()) as ManifestResponse;
  }

  private async deliver(zipResponse: Response, zipName: string, signal: AbortSignal): Promise<void> {
    const useFsAccess = 'showSaveFilePicker' in window;
    if (!CartDownloadService.loggedDeliveryPath) {
      CartDownloadService.loggedDeliveryPath = true;
      console.info(
        useFsAccess
          ? '[CartDownloadService] using showSaveFilePicker (streaming to disk)'
          : '[CartDownloadService] using blob fallback (in-memory ZIP)'
      );
    }

    if (useFsAccess && zipResponse.body) {
      // Streaming path: pipe ZIP bytes directly to a user-chosen file.
      try {
        const handle = await (window as unknown as {
          showSaveFilePicker: (opts: {
            suggestedName?: string;
            types?: { description: string; accept: Record<string, string[]> }[];
          }) => Promise<FileSystemFileHandle>;
        }).showSaveFilePicker({
          suggestedName: zipName,
          types: [{ description: 'ZIP archive', accept: { 'application/zip': ['.zip'] } }]
        });
        const writable = await handle.createWritable();
        // Wire the abort signal so cancellation closes the writable.
        signal.addEventListener('abort', () => { writable.abort().catch(() => {}); }, { once: true });
        await zipResponse.body.pipeTo(writable);
        return;
      } catch (err: unknown) {
        // User cancelled the OS save dialog → AbortError. Treat as a silent abort.
        if (err instanceof DOMException && err.name === 'AbortError') {
          throw err;
        }
        // Other failures fall through to blob fallback.
      }
    }

    // Blob fallback (Firefox / Safari, or showSaveFilePicker rejected):
    // buffer ZIP fully then trigger a synthetic download.
    const blob = await zipResponse.blob();
    if (signal.aborted) return;
    const objectUrl = URL.createObjectURL(blob);
    try {
      const link = document.createElement('a');
      link.href = objectUrl;
      link.download = zipName;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    } finally {
      URL.revokeObjectURL(objectUrl);
    }
  }
}
