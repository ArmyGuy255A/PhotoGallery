import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  IHttpConnectionOptions,
  LogLevel
} from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { AuthService, TokenType } from './auth.service';

/**
 * Quality variants emitted by the backend processing pipeline. Matches the
 * server's <c>Quality</c> enum string serialisation. <c>watermark</c> is the
 * final per-photo "fully ready" signal (also surfaces as <c>WatermarkCompleted</c>).
 */
export type Quality = 'thumbnail' | 'low' | 'medium' | 'high' | 'watermark';

export const QUALITIES: ReadonlyArray<Quality> = ['thumbnail', 'low', 'medium', 'high', 'watermark'];

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** Wire shape of a <c>ProcessingStarted</c> SignalR event. */
export interface ProcessingStartedEvent {
  photoId: string;
  quality?: Quality;
}

/** Wire shape of a <c>ProcessingProgress</c> SignalR event. */
export interface ProcessingProgressEvent {
  photoId: string;
  quality: Quality;
  percent: number;
}

/** Wire shape of a <c>ProcessingCompleted</c> SignalR event. */
export interface ProcessingCompletedEvent {
  photoId: string;
  quality: Quality;
  success: boolean;
  blobPath?: string;
  error?: string;
}

/** Wire shape of a <c>WatermarkCompleted</c> SignalR event. */
export interface WatermarkCompletedEvent {
  photoId: string;
  success: boolean;
  error?: string;
}

/**
 * Server-callable snapshot returned by <c>RequestStatus(photoId)</c>. Used on
 * (re)connect to backfill state for photos that finished while the client was
 * disconnected.
 */
export interface PhotoStatusSnapshot {
  photoId: string;
  perQualityPercent: Partial<Record<Quality, number>>;
  watermarkCompleted: boolean;
  error?: string;
}

/**
 * Real-time progress for in-flight photo processing. Pushes are received from
 * the backend <c>PhotoProgressHub</c> at <c>/hubs/photo-progress</c>; the
 * service maintains three Signals that the upload UI consumes directly.
 *
 * State invariants:
 *   - <c>progress[photoId][quality]</c> is monotonic-max — never regresses on
 *     out-of-order frames.
 *   - When a photoId lands in <c>completed</c> (WatermarkCompleted success) or
 *     <c>errors</c>, it is removed from the in-flight cohort used for the
 *     post-reconnect <c>RequestStatus</c> catchup.
 *   - <c>connectionState</c> mirrors the underlying SignalR lifecycle.
 *
 * This service replaces the per-photo 2s polling loop (which DoS'd the browser
 * with <c>ERR_INSUFFICIENT_RESOURCES</c> at ~500 concurrent uploads).
 */
@Injectable({ providedIn: 'root' })
export class PhotoProgressService {
  private readonly auth = inject(AuthService);

  /** Per-photo per-quality percent (0..100). Monotonic-max on update. */
  readonly progress = signal<Map<string, Partial<Record<Quality, number>>>>(new Map());
  /** Photo IDs whose <c>WatermarkCompleted success</c> event has been seen. */
  readonly completed = signal<Set<string>>(new Set());
  /** Photo IDs that failed at some processing stage, keyed to a short message. */
  readonly errors = signal<Map<string, string>>(new Map());
  /** Mirror of the SignalR hub's lifecycle (UI uses this for a banner). */
  readonly connectionState = signal<ConnectionState>('disconnected');

  private hub: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;

  /**
   * Lazy-open. The hub is started on first <c>start()</c> call (typically from
   * <c>photo-upload.component</c>'s ngOnInit, but the album-detail mid-process
   * view will call it too). Bootstrap-time eager open isn't appropriate
   * because the SPA also serves anonymous code-gallery routes that never need
   * the hub.
   */
  async start(): Promise<void> {
    if (this.hub && this.hub.state !== HubConnectionState.Disconnected) {
      return;
    }
    if (this.startPromise) {
      return this.startPromise;
    }

    const hub = this.buildHub();
    this.hub = hub;
    this.wireEvents(hub);

    this.connectionState.set('connecting');
    this.startPromise = (async () => {
      try {
        await hub.start();
        this.connectionState.set('connected');
      } catch (err) {
        console.error('[PhotoProgressService] hub.start() failed', err);
        this.connectionState.set('disconnected');
        throw err;
      } finally {
        this.startPromise = null;
      }
    })();
    return this.startPromise;
  }

  /** Graceful disconnect — used by tests + future "logout" flow. */
  async stop(): Promise<void> {
    if (!this.hub) return;
    try {
      await this.hub.stop();
    } finally {
      this.connectionState.set('disconnected');
    }
  }

  /**
   * Returns the in-flight cohort: photos with a known progress entry that
   * haven't terminated (neither completed nor errored). Used by the
   * post-reconnect catchup loop.
   */
  inFlightPhotoIds(): string[] {
    const done = this.completed();
    const errs = this.errors();
    const result: string[] = [];
    for (const id of this.progress().keys()) {
      if (!done.has(id) && !errs.has(id)) result.push(id);
    }
    return result;
  }

  /**
   * Test/manual hook: force a snapshot pull for a single photo. Catches up
   * progress + terminal state from the server. No-ops if the hub is not
   * currently connected — the auto-reconnect handler will retry.
   */
  async requestStatus(photoId: string): Promise<void> {
    if (!this.hub || this.hub.state !== HubConnectionState.Connected) return;
    try {
      const snapshot = await this.hub.invoke<PhotoStatusSnapshot | null>('RequestStatus', photoId);
      if (snapshot) this.applySnapshot(snapshot);
    } catch (err) {
      console.warn('[PhotoProgressService] RequestStatus failed for', photoId, err);
    }
  }

  // ---------------------------------------------------------------------------
  // Hub wiring
  // ---------------------------------------------------------------------------

  /**
   * Builds the underlying <c>HubConnection</c>. Extracted as a protected method
   * so unit tests can subclass and substitute a fake hub without opening a
   * real WebSocket connection.
   */
  protected buildHub(): HubConnection {
    const options: IHttpConnectionOptions = {
      accessTokenFactory: () => this.auth.getToken(TokenType.AppToken) ?? '',
      withCredentials: false
    };
    return new HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/photo-progress`, options)
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();
  }

  private wireEvents(hub: HubConnection): void {
    hub.on('ProcessingStarted', (evt: ProcessingStartedEvent) => this.onStarted(evt));
    hub.on('ProcessingProgress', (evt: ProcessingProgressEvent) => this.onProgress(evt));
    hub.on('ProcessingCompleted', (evt: ProcessingCompletedEvent) => this.onCompleted(evt));
    hub.on('WatermarkCompleted', (evt: WatermarkCompletedEvent) => this.onWatermark(evt));

    hub.onreconnecting(() => this.connectionState.set('reconnecting'));
    hub.onreconnected(async () => {
      this.connectionState.set('connected');
      await this.catchUpInFlight();
    });
    hub.onclose(() => this.connectionState.set('disconnected'));
  }

  /** Public for testability — applies a server-side snapshot to local state. */
  applySnapshot(snapshot: PhotoStatusSnapshot): void {
    const photoId = snapshot.photoId;
    for (const [quality, percent] of Object.entries(snapshot.perQualityPercent)) {
      if (typeof percent === 'number') {
        this.bumpPercent(photoId, quality as Quality, percent);
      }
    }
    if (snapshot.watermarkCompleted) {
      this.markCompleted(photoId);
    }
    if (snapshot.error) {
      this.markError(photoId, snapshot.error);
    }
  }

  // ---------------------------------------------------------------------------
  // Event handlers (also exposed as public methods so component tests can
  // drive the service via direct calls instead of mocking the full hub.)
  // ---------------------------------------------------------------------------

  onStarted(evt: ProcessingStartedEvent): void {
    // Seed the photoId entry. If a quality is named, also seed that bar at 0
    // (so the UI renders an empty bar immediately rather than missing the
    // row). If no quality (server's initial fire from /upload-complete),
    // just create the photoId entry.
    this.progress.update(prev => {
      const next = new Map(prev);
      const cur = next.get(evt.photoId) ?? {};
      const updated = { ...cur };
      if (evt.quality && updated[evt.quality] === undefined) {
        updated[evt.quality] = 0;
      }
      next.set(evt.photoId, updated);
      return next;
    });
  }

  onProgress(evt: ProcessingProgressEvent): void {
    this.bumpPercent(evt.photoId, evt.quality, evt.percent);
  }

  onCompleted(evt: ProcessingCompletedEvent): void {
    if (evt.success) {
      this.bumpPercent(evt.photoId, evt.quality, 100);
      // A successful completion clears any prior error for this photo;
      // a later quality may still error, but per-quality successes shouldn't
      // be shadowed by an earlier transient.
      this.clearError(evt.photoId);
    } else {
      this.markError(evt.photoId, evt.error ?? `Processing failed for ${evt.quality}`);
    }
  }

  onWatermark(evt: WatermarkCompletedEvent): void {
    if (evt.success) {
      this.bumpPercent(evt.photoId, 'watermark', 100);
      this.markCompleted(evt.photoId);
      this.clearError(evt.photoId);
    } else {
      this.markError(evt.photoId, evt.error ?? 'Watermark generation failed');
    }
  }

  /**
   * Reset everything (e.g. when navigating away from the upload view). Tests
   * also use this between cases.
   */
  reset(): void {
    this.progress.set(new Map());
    this.completed.set(new Set());
    this.errors.set(new Map());
  }

  // ---------------------------------------------------------------------------
  // Internals
  // ---------------------------------------------------------------------------

  private bumpPercent(photoId: string, quality: Quality, percent: number): void {
    if (typeof percent !== 'number' || Number.isNaN(percent)) return;
    const clamped = Math.max(0, Math.min(100, percent));

    this.progress.update(prev => {
      const next = new Map(prev);
      const cur = next.get(photoId) ?? {};
      const existing = cur[quality];
      // Monotonic-max — out-of-order frames must never regress the bar.
      if (existing !== undefined && existing >= clamped) {
        next.set(photoId, cur);
        return next;
      }
      next.set(photoId, { ...cur, [quality]: clamped });
      return next;
    });
  }

  private markCompleted(photoId: string): void {
    this.completed.update(prev => {
      if (prev.has(photoId)) return prev;
      const next = new Set(prev);
      next.add(photoId);
      return next;
    });
  }

  private markError(photoId: string, message: string): void {
    this.errors.update(prev => {
      const next = new Map(prev);
      next.set(photoId, message);
      return next;
    });
  }

  private clearError(photoId: string): void {
    this.errors.update(prev => {
      if (!prev.has(photoId)) return prev;
      const next = new Map(prev);
      next.delete(photoId);
      return next;
    });
  }

  /**
   * Post-reconnect catchup: ask the server for the current snapshot of every
   * photo we still think is in-flight. Catches completions/errors fired
   * during the disconnect window. Best-effort: failures are logged, not
   * surfaced — the next live event will heal local state.
   */
  private async catchUpInFlight(): Promise<void> {
    const ids = this.inFlightPhotoIds();
    if (ids.length === 0) return;
    await Promise.all(ids.map(id => this.requestStatus(id)));
  }
}
