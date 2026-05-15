import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType, HttpHeaders } from '@angular/common/http';
import { Observable, concat, defer, of, throwError } from 'rxjs';
import { catchError, switchMap, map, mergeMap, filter, takeWhile } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { AuthService, TokenType } from './auth.service';

export interface Photo {
  id: string;
  fileName: string;
  uploadDate: Date;
  uploadedBy: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

export interface CompressionProfile {
  name: string;
  qualityPercentage: number;
  description: string;
}

export interface UploadPhotoResponse {
  successfulUploads: any[];
  errors: string[];
  totalUploaded: number;
  totalFailed: number;
}

export interface ProcessingStatus {
  photoId: string;
  status: string;
  completedVersions: number;
  totalVersions: number;
  percentComplete: number;
  processingStartedAt?: Date;
  processingCompletedAt?: Date;
  hasThumbnail: boolean;
  hasLow: boolean;
  hasMedium: boolean;
  hasHigh: boolean;
}

/**
 * Per-file request body for <c>POST /api/photos/albums/{id}/upload-tickets</c>.
 * The SPA submits a batch (one entry per file); the controller mints a
 * write-only SAS for each and returns parallel <see cref="UploadTicketResponse"/>
 * entries.
 */
export interface UploadTicketRequest {
  fileName: string;
  contentType: string;
  size: number;
}

/**
 * Per-file response from <c>POST /api/photos/albums/{id}/upload-tickets</c>.
 * The SPA PUTs the file bytes to <see cref="uploadUrl"/> (a write-only SAS,
 * 15-minute TTL) and then calls <c>POST /api/photos/{photoId}/upload-complete</c>.
 */
export interface UploadTicketResponse {
  photoId: string;
  uploadUrl: string;
  blobPath: string;
  expiresAt: string;
}

/**
 * Per-file "already complete" entry from <c>POST /api/photos/albums/{id}/upload-tickets</c>.
 * Returned when a duplicate filename already exists in the album in any
 * non-Uploading status. The SPA renders the row as queued/done immediately
 * and skips the PUT + upload-complete dance.
 */
export interface CompletedUploadTicket {
  photoId: string;
  fileName: string;
}

/**
 * Envelope returned by <c>POST /api/photos/albums/{id}/upload-tickets</c>.
 * Always 200 OK. <see cref="tickets"/> are files the SPA should upload;
 * <see cref="alreadyComplete"/> are duplicate filenames whose existing row
 * is already past <c>Uploading</c> — skip the upload and surface the row as
 * done.
 */
export interface UploadTicketsResponse {
  tickets: UploadTicketResponse[];
  alreadyComplete: CompletedUploadTicket[];
}

export interface UploadCompleteResponse {
  photoId: string;
  status: string;
}

/**
 * Discriminated union describing the lifecycle of a single direct-to-blob
 * upload. Components subscribe to a stream of these to drive the per-file
 * progress bar and final "queued for processing" state.
 *
 *   ticket           → /upload-tickets request in flight
 *   uploading        → PUT to storage in flight (bytesSent/bytesTotal tracks progress)
 *   completing       → /upload-complete request in flight
 *   queued           → server accepted; processing has been scheduled
 *   alreadyComplete  → duplicate filename; server returned the existing
 *                      photoId, no PUT happened. UI should mark the row as
 *                      "already in album, skipped" rather than success.
 *   error            → terminal failure at any step (message + optional photoId so
 *                      the component can mark which row failed)
 */
export type UploadProgress =
  | { phase: 'ticket' }
  | { phase: 'uploading'; photoId: string; bytesSent: number; bytesTotal: number }
  | { phase: 'completing'; photoId: string }
  | { phase: 'queued'; photoId: string }
  | { phase: 'alreadyComplete'; photoId: string; fileName: string }
  | { phase: 'error'; photoId?: string; message: string };

/** Max in-flight direct-to-blob PUTs for a batched <see cref="PhotoService.uploadPhotos"/> call. */
const UPLOAD_CONCURRENCY = 6;

/**
 * Service for managing photos
 */
@Injectable({
  providedIn: 'root'
})
export class PhotoService {
  private readonly API_URL = `${environment.apiUrl}/api/photos`;

  constructor(private http: HttpClient, private authService: AuthService) {}

  /**
   * Upload a single photo using the 3-call direct-to-blob flow:
   *
   *   1. <c>POST /api/photos/albums/{id}/upload-tickets</c> — mint a write SAS
   *   2. <c>PUT &lt;sas-url&gt;</c> — browser → storage, server never sees bytes
   *   3. <c>POST /api/photos/{photoId}/upload-complete</c> — schedule processing
   *
   * Emits a stream of <see cref="UploadProgress"/> events so callers can drive
   * a progress bar and detect the terminal state (<c>queued</c> or <c>error</c>).
   * The observable completes after the terminal event — callers do not need
   * to track subscriptions beyond that.
   *
   * The PUT in step 2 bypasses the JWT interceptor (see
   * <see cref="jwtInterceptor"/>: pre-signed URLs are detected by their
   * <c>sig=</c> / <c>X-Amz-Signature=</c> query param) so the SAS signature
   * isn't shadowed by an Authorization header storage doesn't expect.
   */
  uploadPhoto(albumId: string, file: File): Observable<UploadProgress> {
    const ticketReq: UploadTicketRequest = {
      fileName: file.name,
      contentType: file.type || 'application/octet-stream',
      size: file.size
    };

    const stream$: Observable<UploadProgress> = concat(
      // 1. ticket request: emit the 'ticket' phase synchronously, then fold in
      //    the HTTP POST so a failure becomes an error event downstream.
      of<UploadProgress>({ phase: 'ticket' }),
      this.http
        .post<UploadTicketsResponse>(`${this.API_URL}/albums/${albumId}/upload-tickets`, [ticketReq])
        .pipe(
          switchMap(response => {
            const done = response?.alreadyComplete?.[0];
            if (done) {
              // Duplicate of an existing non-Uploading photo. Surface a
              // distinct alreadyComplete event so the UI can render the row
              // as "skipped, already in album" rather than a fresh success.
              return of<UploadProgress>({
                phase: 'alreadyComplete',
                photoId: done.photoId,
                fileName: done.fileName
              });
            }
            const ticket = response?.tickets?.[0];
            if (!ticket) {
              return throwError(() => new Error('Upload ticket response was empty'));
            }
            const photoId = ticket.photoId;
            // 2. PUT to storage, mapping HttpProgressEvents to 'uploading' frames;
            //    3. then a single 'completing' marker; 4. then the 'queued' event
            //    from the upload-complete POST. concat ensures strict ordering.
            const put$ = this.putToStorage(ticket, file).pipe(
              map(evt =>
                evt.kind === 'progress'
                  ? ({
                      phase: 'uploading',
                      photoId,
                      bytesSent: evt.bytesSent,
                      bytesTotal: evt.bytesTotal
                    } as UploadProgress)
                  : null
              ),
              filter((p): p is UploadProgress => p !== null),
              catchError(err => throwError(() => this.tagError(err, photoId)))
            );
            const complete$ = defer(() =>
              concat(
                of<UploadProgress>({ phase: 'completing', photoId }),
                this.completeUpload(photoId, file.size).pipe(
                  catchError(err => throwError(() => this.tagError(err, photoId)))
                )
              )
            );
            return concat(put$, complete$);
          })
        )
    );

    return stream$.pipe(
      catchError(err => {
        const e = err as { photoId?: string; message?: string };
        return of<UploadProgress>({
          phase: 'error',
          photoId: e?.photoId,
          message: this.errorMessage(err)
        });
      })
    );
  }

  /**
   * Batch wrapper around <see cref="uploadPhoto"/>. Each input file gets its
   * own <see cref="UploadProgress"/> stream; the streams are flattened with a
   * concurrency cap so the browser doesn't open dozens of simultaneous PUTs
   * for a large batch. Components can demultiplex by <c>photoId</c> (or by
   * subscribing per file via <see cref="uploadPhoto"/> directly).
   */
  uploadPhotosStream(albumId: string, files: File[]): Observable<UploadProgress> {
    return of(...files).pipe(
      mergeMap(file => this.uploadPhoto(albumId, file), UPLOAD_CONCURRENCY)
    );
  }

  /**
   * Legacy batch upload used by tests and the multipart fallback path. Kept
   * for backwards compatibility — new callers should prefer
   * <see cref="uploadPhoto"/> or <see cref="uploadPhotosStream"/> which use
   * the direct-to-blob flow.
   *
   * @deprecated Use the SAS-based flow; this multipart POST routes all bytes
   * through the API host.
   */
  uploadPhotos(albumId: string, files: File[]): Observable<UploadPhotoResponse> {
    const formData = new FormData();
    files.forEach(file => {
      formData.append('files', file);
    });

    return this.http.post<UploadPhotoResponse>(
      `${this.API_URL}/albums/${albumId}`,
      formData
    );
  }

  /**
   * Pipes Angular's HttpClient PUT events into a small internal union so the
   * outer pipeline doesn't need to know about HttpEventType. Emits
   * <c>{ kind: 'progress', ... }</c> for each UploadProgress event and a final
   * <c>{ kind: 'completed' }</c> when the server responds.
   */
  private putToStorage(
    ticket: UploadTicketResponse,
    file: File
  ): Observable<{ kind: 'progress'; bytesSent: number; bytesTotal: number } | { kind: 'completed' }> {
    // Azure Blob requires `x-ms-blob-type: BlockBlob` for a simple PUT under
    // the single-shot 256-MiB ceiling. MinIO ignores the header so it's safe
    // to send unconditionally.
    const headers = new HttpHeaders({
      'x-ms-blob-type': 'BlockBlob',
      'Content-Type': file.type || 'application/octet-stream'
    });

    return this.http
      .put(ticket.uploadUrl, file, {
        headers,
        reportProgress: true,
        observe: 'events',
        responseType: 'text'
      })
      .pipe(
        map((evt: HttpEvent<string>) => {
          if (evt.type === HttpEventType.UploadProgress) {
            return {
              kind: 'progress' as const,
              bytesSent: evt.loaded,
              bytesTotal: evt.total ?? file.size
            };
          }
          if (evt.type === HttpEventType.Response) {
            return { kind: 'completed' as const };
          }
          return null;
        }),
        filter((v): v is { kind: 'progress'; bytesSent: number; bytesTotal: number } | { kind: 'completed' } => v !== null),
        // Stop after the response marker so the observable completes promptly
        // even if HttpClient would emit additional events.
        takeWhile(v => v.kind !== 'completed', true)
      );
  }

  /**
   * POST /api/photos/{id}/upload-complete. Emits a single <c>queued</c>
   * progress event and then completes (the server flips the row to Pending
   * and queues the processing items).
   */
  private completeUpload(photoId: string, actualSize: number): Observable<UploadProgress> {
    return this.http
      .post<UploadCompleteResponse>(`${this.API_URL}/${photoId}/upload-complete`, { actualSize })
      .pipe(map(() => ({ phase: 'queued', photoId } as UploadProgress)));
  }

  private errorMessage(err: unknown): string {
    if (!err) return 'Upload failed';
    if (typeof err === 'string') return err;
    const e = err as {
      error?: { message?: string; code?: string };
      message?: string;
      statusText?: string;
    };
    return e.error?.message || e.message || e.statusText || 'Upload failed';
  }

  /**
   * Attach the photoId to an error so the outer catchError can surface it on
   * the terminal <c>{ phase: 'error', photoId }</c> event — components need
   * to know which row to mark failed when the PUT or upload-complete blows
   * up partway through a batch.
   */
  private tagError(err: unknown, photoId: string): { photoId: string; message: string; original: unknown } {
    return {
      photoId,
      message: this.errorMessage(err),
      original: err
    };
  }

  /**
   * Download photo by quality
   */
  downloadPhoto(photoId: string, quality: string = 'medium'): Observable<Blob> {
    return this.http.get(
      `${this.API_URL}/${photoId}/download?quality=${quality}`,
      { responseType: 'blob' }
    );
  }

  /**
   * Get compression profiles
   */
  getCompressionProfiles(): Observable<CompressionProfile[]> {
    return this.http.get<CompressionProfile[]>(
      `${this.API_URL}/compression-profiles`
    );
  }

  /**
   * Get processing status for a photo by photoId
   */
  getPhotoProcessingStatus(photoId: string): Observable<ProcessingStatus> {
    return this.http.get<ProcessingStatus>(
      `${this.API_URL}/${photoId}/status`
    );
  }

  /**
   * Get thumbnail URL for a photo.
   *
   * Browser-initiated image requests (``<img src=...>``) cannot carry the
   * Authorization header that Angular's HttpClient + jwtInterceptor adds, so
   * we append the AppToken as ``?access_token=...`` query string. Backend's
   * JwtBearer OnMessageReceived handler reads this fallback when the
   * Authorization header is absent. Standard pattern for protected file URLs.
   *
   * Returns an empty string if no AppToken is present (caller should not
   * attempt to render the thumbnail until login completes).
   */
  getThumbnailUrl(photoId: string): string {
    const token = this.authService.getToken(TokenType.AppToken);
    if (!token) return '';
    const t = encodeURIComponent(token);
    return `${this.API_URL}/${photoId}/download?quality=Thumbnail&access_token=${t}`;
  }

  /**
   * NOTE: <c>pollProcessingStatus</c> was removed when SignalR replaced the
   * per-photo 2-second polling loop (Phase 3). The single-shot
   * <see cref="getPhotoProcessingStatus"/> above is retained for snapshot
   * use cases (e.g. album-detail loading mid-processing photos on first
   * render); callers must NOT wrap it in <c>setInterval</c> / RxJS
   * <c>interval()</c>. Subscribe to <c>PhotoProgressService</c> instead.
   */

  /**
   * Download photo via access code
   */
  downloadPhotoByCode(code: string, photoId: string, quality: string = 'medium'): Observable<Blob> {
    return this.http.get(
      `${environment.apiUrl}/api/code/${code}/photo/${photoId}/download?quality=${quality}`,
      { responseType: 'blob' }
    );
  }

  /**
   * Get photos via access code
   */
  getPhotosByCode(code: string): Observable<Photo[]> {
    return this.http.get<Photo[]>(`${environment.apiUrl}/api/code/${code}/photos`);
  }
}
