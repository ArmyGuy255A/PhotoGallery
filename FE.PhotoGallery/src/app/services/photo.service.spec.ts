import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpEventType, HttpHandler, HttpRequest, HttpResponse, HttpStatusCode, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PhotoService, UploadProgress, UploadTicketResponse } from './photo.service';
import { AuthService, TokenType } from './auth.service';
import { jwtInterceptor, isPresignedStorageUrl } from './jwt.interceptor';
import { environment } from '../../environments/environment';

class AuthServiceStub {
  getToken(_type: TokenType): string | null { return 'fake-jwt'; }
}

describe('PhotoService — SAS upload flow', () => {
  let service: PhotoService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useClass: AuthServiceStub },
        PhotoService
      ]
    });
    service = TestBed.inject(PhotoService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function makeFile(name = 'pic.jpg', size = 1024, type = 'image/jpeg'): File {
    const blob = new Blob([new Uint8Array(size)], { type });
    return new File([blob], name, { type });
  }

  it('issues ticket → SAS PUT → upload-complete in order and emits the expected progress events', () => {
    const file = makeFile('a.jpg', 500);
    const albumId = 'album-123';
    const events: UploadProgress[] = [];
    let completed = false;

    service.uploadPhoto(albumId, file).subscribe({
      next: e => events.push(e),
      complete: () => (completed = true)
    });

    // Step 1: ticket POST
    const ticketReq = httpMock.expectOne(`${environment.apiUrl}/api/photos/albums/${albumId}/upload-tickets`);
    expect(ticketReq.request.method).toBe('POST');
    expect(ticketReq.request.body).toEqual([{ fileName: 'a.jpg', contentType: 'image/jpeg', size: 500 }]);
    const ticket: UploadTicketResponse = {
      photoId: 'photo-1',
      uploadUrl: 'https://storage.example.com/blobs/photo-1?sv=2023-01-01&sig=abc',
      blobPath: 'blobs/photo-1',
      expiresAt: new Date().toISOString()
    };
    ticketReq.flush({ tickets: [ticket], alreadyComplete: [] });

    // Step 2: PUT to SAS URL
    const putReq = httpMock.expectOne(ticket.uploadUrl);
    expect(putReq.request.method).toBe('PUT');
    expect(putReq.request.headers.get('x-ms-blob-type')).toBe('BlockBlob');
    expect(putReq.request.headers.get('Content-Type')).toBe('image/jpeg');
    // Interceptor must NOT add an Authorization header to the SAS PUT.
    expect(putReq.request.headers.get('Authorization')).toBeNull();
    // Emit a progress event, then the final response.
    putReq.event({ type: HttpEventType.UploadProgress, loaded: 250, total: 500 });
    putReq.flush('', { status: HttpStatusCode.Created, statusText: 'Created' });

    // Step 3: upload-complete POST
    const completeReq = httpMock.expectOne(`${environment.apiUrl}/api/photos/photo-1/upload-complete`);
    expect(completeReq.request.method).toBe('POST');
    expect(completeReq.request.body).toEqual({ actualSize: 500 });
    // JWT IS attached for the API request.
    expect(completeReq.request.headers.get('Authorization')).toBe('Bearer fake-jwt');
    completeReq.flush({ photoId: 'photo-1', status: 'Pending' });

    expect(completed).toBeTrue();
    const phases = events.map(e => e.phase);
    expect(phases).toEqual(['ticket', 'uploading', 'completing', 'queued']);
    const uploading = events[1];
    if (uploading.phase === 'uploading') {
      expect(uploading.bytesSent).toBe(250);
      expect(uploading.bytesTotal).toBe(500);
      expect(uploading.photoId).toBe('photo-1');
    } else {
      fail('expected uploading event');
    }
    const queued = events[3];
    if (queued.phase === 'queued') {
      expect(queued.photoId).toBe('photo-1');
    } else {
      fail('expected queued event');
    }
  });

  it('emits { phase: "error", photoId } when the PUT to storage fails', () => {
    const file = makeFile();
    const events: UploadProgress[] = [];
    let completed = false;
    service.uploadPhoto('a', file).subscribe({ next: e => events.push(e), complete: () => (completed = true) });

    const ticketReq = httpMock.expectOne(`${environment.apiUrl}/api/photos/albums/a/upload-tickets`);
    ticketReq.flush({
      tickets: [{
        photoId: 'photo-err',
        uploadUrl: 'https://storage.example.com/p?sig=xyz',
        blobPath: 'p',
        expiresAt: new Date().toISOString()
      }],
      alreadyComplete: []
    });

    const putReq = httpMock.expectOne('https://storage.example.com/p?sig=xyz');
    putReq.flush('forbidden', { status: 403, statusText: 'Forbidden' });

    expect(completed).toBeTrue();
    const last = events[events.length - 1];
    expect(last.phase).toBe('error');
    if (last.phase === 'error') {
      expect(last.photoId).toBe('photo-err');
      expect(last.message).toBeTruthy();
    }
  });

  it('emits { phase: "error" } when the ticket request fails', () => {
    const file = makeFile();
    const events: UploadProgress[] = [];
    service.uploadPhoto('a', file).subscribe({ next: e => events.push(e) });

    const ticketReq = httpMock.expectOne(`${environment.apiUrl}/api/photos/albums/a/upload-tickets`);
    ticketReq.flush('boom', { status: 500, statusText: 'ISE' });

    const last = events[events.length - 1];
    expect(last.phase).toBe('error');
  });

  it('emits queued (no PUT) when the server reports the file as already complete', () => {
    const file = makeFile();
    const events: UploadProgress[] = [];
    let completed = false;
    service.uploadPhoto('a', file).subscribe({
      next: e => events.push(e),
      complete: () => (completed = true)
    });

    const ticketReq = httpMock.expectOne(`${environment.apiUrl}/api/photos/albums/a/upload-tickets`);
    ticketReq.flush({
      tickets: [],
      alreadyComplete: [{ photoId: 'existing-photo-id', fileName: file.name }]
    });

    // No PUT to storage should be issued.
    httpMock.expectNone(req => req.method === 'PUT');

    expect(completed).toBeTrue();
    const phases = events.map(e => e.phase);
    expect(phases).toEqual(['ticket', 'queued']);
    const queued = events[1];
    if (queued.phase === 'queued') {
      expect(queued.photoId).toBe('existing-photo-id');
    } else {
      fail('expected queued event');
    }
  });

  it('isPresignedStorageUrl detects Azure Blob and S3/MinIO signature params', () => {
    expect(isPresignedStorageUrl('https://acct.blob.core.windows.net/c/k?sv=2023&sig=abc')).toBeTrue();
    expect(isPresignedStorageUrl('https://minio.local/bucket/k?X-Amz-Signature=def&X-Amz-Date=2024')).toBeTrue();
    expect(isPresignedStorageUrl(`${environment.apiUrl}/api/photos/x/upload-complete`)).toBeFalse();
    expect(isPresignedStorageUrl(`${environment.apiUrl}/api/albums`)).toBeFalse();
  });
});
