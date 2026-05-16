import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';
import { PhotoProgressService, PhotoStatusSnapshot } from './photo-progress.service';
import { AuthService, TokenType } from './auth.service';

/**
 * A minimal stand-in for SignalR's <c>HubConnection</c> that drives the
 * service's connectionState lifecycle deterministically. Only members touched
 * by <c>PhotoProgressService</c> are implemented; the rest are <c>any</c>-cast
 * to satisfy the structural type at the assignment site.
 */
class FakeHub {
  state: 'Disconnected' | 'Connecting' | 'Connected' | 'Reconnecting' = 'Disconnected';
  private handlers = new Map<string, (arg: unknown) => void>();
  private reconnectingCb: () => void = () => {};
  private reconnectedCb: () => Promise<void> | void = () => {};
  private closedCb: () => void = () => {};

  /** @internal — set by the unit test to script <c>RequestStatus</c> returns. */
  snapshotsByPhotoId = new Map<string, PhotoStatusSnapshot>();
  /** @internal — counts <c>invoke('RequestStatus', ...)</c> calls. */
  requestStatusInvocations: string[] = [];

  on(method: string, cb: (arg: unknown) => void) {
    this.handlers.set(method, cb);
  }
  onreconnecting(cb: () => void) { this.reconnectingCb = cb; }
  onreconnected(cb: () => void | Promise<void>) { this.reconnectedCb = cb; }
  onclose(cb: () => void) { this.closedCb = cb; }

  async start(): Promise<void> {
    this.state = 'Connected';
  }
  async stop(): Promise<void> {
    this.state = 'Disconnected';
    this.closedCb();
  }
  async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    if (method === 'RequestStatus') {
      const photoId = String(args[0]);
      this.requestStatusInvocations.push(photoId);
      return (this.snapshotsByPhotoId.get(photoId) ?? null) as T;
    }
    throw new Error(`FakeHub: unhandled invoke ${method}`);
  }

  // Test-only hooks
  fire(method: string, evt: unknown) {
    this.handlers.get(method)?.(evt);
  }
  triggerReconnecting() {
    this.state = 'Reconnecting';
    this.reconnectingCb();
  }
  async triggerReconnected() {
    this.state = 'Connected';
    await this.reconnectedCb();
  }
  triggerClose() {
    this.state = 'Disconnected';
    this.closedCb();
  }
}

/** Subclass that hands out a FakeHub instead of opening a real WebSocket. */
class TestablePhotoProgressService extends PhotoProgressService {
  readonly fakeHub = new FakeHub();
  protected override buildHub(): HubConnection {
    return this.fakeHub as unknown as HubConnection;
  }
}

describe('PhotoProgressService — connection lifecycle', () => {
  let service: TestablePhotoProgressService;
  let authSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authSpy = jasmine.createSpyObj<AuthService>('AuthService', ['getToken']);
    authSpy.getToken.and.callFake((t: TokenType) =>
      t === TokenType.AppToken ? 'fake-token' : null
    );

    TestBed.configureTestingModule({
      providers: [
        TestablePhotoProgressService,
        { provide: AuthService, useValue: authSpy }
      ]
    });
    service = TestBed.inject(TestablePhotoProgressService);
  });

  it('flips through connecting → connected on start()', async () => {
    expect(service.connectionState()).toBe('disconnected');
    const startPromise = service.start();
    expect(service.connectionState()).toBe('connecting');
    await startPromise;
    expect(service.connectionState()).toBe('connected');
  });

  it('goes to reconnecting on hub.onreconnecting and back to connected on hub.onreconnected', async () => {
    await service.start();

    service.fakeHub.triggerReconnecting();
    expect(service.connectionState()).toBe('reconnecting');

    await service.fakeHub.triggerReconnected();
    expect(service.connectionState()).toBe('connected');
  });

  it('lands at disconnected on hub.onclose', async () => {
    await service.start();
    service.fakeHub.triggerClose();
    expect(service.connectionState()).toBe('disconnected');
  });

  it('re-syncs in-flight photos after reconnect via RequestStatus', async () => {
    await service.start();

    // Build local in-flight state: p1 in progress, p2 completed, p3 errored.
    service.onProgress({ photoId: 'p1', quality: 'medium', percent: 30 });
    service.onProgress({ photoId: 'p2', quality: 'thumbnail', percent: 100 });
    service.onWatermark({ photoId: 'p2', success: true });
    service.onCompleted({
      photoId: 'p3',
      quality: 'high',
      success: false,
      error: 'boom'
    });

    // Script the server response for the only still-in-flight photo (p1):
    // the watermark fired while we were disconnected, so the snapshot now
    // says fully complete.
    service.fakeHub.snapshotsByPhotoId.set('p1', {
      photoId: 'p1',
      perQualityPercent: { thumbnail: 100, low: 100, medium: 100, high: 100, watermark: 100 },
      watermarkCompleted: true
    });

    service.fakeHub.triggerReconnecting();
    expect(service.connectionState()).toBe('reconnecting');

    await service.fakeHub.triggerReconnected();

    expect(service.fakeHub.requestStatusInvocations).toEqual(['p1']);
    expect(service.completed().has('p1')).toBeTrue();
    expect(service.connectionState()).toBe('connected');
  });

  it('routes wire events through the hub binding', async () => {
    await service.start();
    service.fakeHub.fire('ProcessingStarted', { photoId: 'wire', quality: 'low' });
    service.fakeHub.fire('ProcessingProgress', { photoId: 'wire', quality: 'low', percent: 42 });

    expect(service.progress().get('wire')?.low).toBe(42);
  });
});
