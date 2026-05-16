import { TestBed } from '@angular/core/testing';
import {
  PhotoProgressService,
  Quality
} from './photo-progress.service';
import { AuthService, TokenType } from './auth.service';

/**
 * Tests drive the service via its public event handlers (onStarted /
 * onProgress / onCompleted / onWatermark) — this is the same surface the
 * SignalR hub binds to, so the behavioural coverage is identical to driving
 * the real hub. The hub itself is never started in these specs.
 *
 * Lifecycle/connectionState transitions are covered by a separate test that
 * uses a fake hub injected via a subclass override of <c>buildHub()</c>.
 */
describe('PhotoProgressService', () => {
  let service: PhotoProgressService;
  let authSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authSpy = jasmine.createSpyObj<AuthService>('AuthService', ['getToken']);
    authSpy.getToken.and.callFake((t: TokenType) =>
      t === TokenType.AppToken ? 'fake-token' : null
    );

    TestBed.configureTestingModule({
      providers: [
        PhotoProgressService,
        { provide: AuthService, useValue: authSpy }
      ]
    });
    service = TestBed.inject(PhotoProgressService);
  });

  afterEach(() => service.reset());

  // ---------------------------------------------------------------------------
  // ProcessingStarted
  // ---------------------------------------------------------------------------
  describe('ProcessingStarted', () => {
    it('seeds the map at 0% for the named quality', () => {
      service.onStarted({ photoId: 'p1', quality: 'thumbnail' });

      const entry = service.progress().get('p1');
      expect(entry).toBeTruthy();
      expect(entry?.thumbnail).toBe(0);
    });

    it('creates an empty entry when no quality is supplied', () => {
      service.onStarted({ photoId: 'p2' });

      expect(service.progress().has('p2')).toBeTrue();
      const entry = service.progress().get('p2');
      expect(entry).toEqual({});
    });

    it('does not stomp an existing higher percent for the same quality', () => {
      service.onProgress({ photoId: 'p1', quality: 'low', percent: 75 });
      service.onStarted({ photoId: 'p1', quality: 'low' });

      expect(service.progress().get('p1')?.low).toBe(75);
    });
  });

  // ---------------------------------------------------------------------------
  // ProcessingProgress
  // ---------------------------------------------------------------------------
  describe('ProcessingProgress', () => {
    it('updates the percent for a (photoId, quality)', () => {
      service.onProgress({ photoId: 'p1', quality: 'medium', percent: 25 });
      service.onProgress({ photoId: 'p1', quality: 'medium', percent: 50 });

      expect(service.progress().get('p1')?.medium).toBe(50);
    });

    it('never regresses on out-of-order events (monotonic-max)', () => {
      service.onProgress({ photoId: 'p1', quality: 'high', percent: 75 });
      service.onProgress({ photoId: 'p1', quality: 'high', percent: 50 }); // late frame
      service.onProgress({ photoId: 'p1', quality: 'high', percent: 25 }); // even later

      expect(service.progress().get('p1')?.high).toBe(75);
    });

    it('clamps percents into [0, 100]', () => {
      service.onProgress({ photoId: 'p1', quality: 'low', percent: 150 });
      expect(service.progress().get('p1')?.low).toBe(100);

      service.onProgress({ photoId: 'p2', quality: 'low', percent: -10 });
      expect(service.progress().get('p2')?.low).toBe(0);
    });

    it('tracks per-quality bars independently for the same photo', () => {
      const qualities: Quality[] = ['thumbnail', 'low', 'medium', 'high'];
      qualities.forEach((q, i) =>
        service.onProgress({ photoId: 'p1', quality: q, percent: (i + 1) * 20 })
      );

      const entry = service.progress().get('p1')!;
      expect(entry.thumbnail).toBe(20);
      expect(entry.low).toBe(40);
      expect(entry.medium).toBe(60);
      expect(entry.high).toBe(80);
    });
  });

  // ---------------------------------------------------------------------------
  // ProcessingCompleted
  // ---------------------------------------------------------------------------
  describe('ProcessingCompleted', () => {
    it('bumps the quality to 100% when success=true', () => {
      service.onProgress({ photoId: 'p1', quality: 'medium', percent: 50 });
      service.onCompleted({ photoId: 'p1', quality: 'medium', success: true });

      expect(service.progress().get('p1')?.medium).toBe(100);
    });

    it('clears any prior error on success', () => {
      service.onCompleted({
        photoId: 'p1',
        quality: 'medium',
        success: false,
        error: 'transient blip'
      });
      expect(service.errors().get('p1')).toBe('transient blip');

      service.onCompleted({ photoId: 'p1', quality: 'medium', success: true });
      expect(service.errors().has('p1')).toBeFalse();
    });

    it('records an error when success=false', () => {
      service.onCompleted({
        photoId: 'p1',
        quality: 'high',
        success: false,
        error: 'codec exploded'
      });

      expect(service.errors().get('p1')).toBe('codec exploded');
    });

    it('records a default error message when none is provided', () => {
      service.onCompleted({ photoId: 'p1', quality: 'low', success: false });
      expect(service.errors().get('p1')).toContain('low');
    });
  });

  // ---------------------------------------------------------------------------
  // WatermarkCompleted
  // ---------------------------------------------------------------------------
  describe('WatermarkCompleted', () => {
    it('adds the photoId to completed on success', () => {
      service.onWatermark({ photoId: 'p1', success: true });

      expect(service.completed().has('p1')).toBeTrue();
      expect(service.progress().get('p1')?.watermark).toBe(100);
    });

    it('records an error on failure (does NOT add to completed)', () => {
      service.onWatermark({ photoId: 'p1', success: false, error: 'font missing' });

      expect(service.completed().has('p1')).toBeFalse();
      expect(service.errors().get('p1')).toBe('font missing');
    });
  });

  // ---------------------------------------------------------------------------
  // inFlightPhotoIds — drives the post-reconnect catchup loop
  // ---------------------------------------------------------------------------
  describe('inFlightPhotoIds', () => {
    it('returns photos with progress but not completed and not errored', () => {
      service.onStarted({ photoId: 'inflight', quality: 'thumbnail' });
      service.onProgress({ photoId: 'done', quality: 'thumbnail', percent: 100 });
      service.onWatermark({ photoId: 'done', success: true });
      service.onCompleted({
        photoId: 'failed',
        quality: 'high',
        success: false,
        error: 'x'
      });

      const ids = service.inFlightPhotoIds();
      expect(ids).toContain('inflight');
      expect(ids).not.toContain('done');
      expect(ids).not.toContain('failed');
    });
  });

  // ---------------------------------------------------------------------------
  // applySnapshot — what the post-reconnect RequestStatus call feeds in
  // ---------------------------------------------------------------------------
  describe('applySnapshot', () => {
    it('merges per-quality percents and terminal state', () => {
      service.applySnapshot({
        photoId: 'p1',
        perQualityPercent: { thumbnail: 100, low: 50 },
        watermarkCompleted: true
      });

      expect(service.progress().get('p1')?.thumbnail).toBe(100);
      expect(service.progress().get('p1')?.low).toBe(50);
      expect(service.completed().has('p1')).toBeTrue();
    });

    it('does not regress local state if local has higher percent', () => {
      service.onProgress({ photoId: 'p1', quality: 'high', percent: 90 });
      service.applySnapshot({
        photoId: 'p1',
        perQualityPercent: { high: 50 },
        watermarkCompleted: false
      });

      expect(service.progress().get('p1')?.high).toBe(90);
    });
  });
});
