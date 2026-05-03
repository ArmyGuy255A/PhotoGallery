import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';

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
 * Service for managing photos
 */
@Injectable({
  providedIn: 'root'
})
export class PhotoService {
  private readonly API_URL = `${environment.apiUrl}/api/photos`;

  constructor(private http: HttpClient) {}

  /**
   * Upload a single photo to album
   */
  uploadPhoto(albumId: string, file: File): Observable<UploadPhotoResponse> {
    const formData = new FormData();
    formData.append('files', file);

    const url = `${this.API_URL}/albums/${albumId}`;
    console.log(`[PhotoService] Uploading ${file.name} to ${url}`);
    
    return this.http.post<UploadPhotoResponse>(url, formData).pipe(
      tap(() => console.log(`[PhotoService] Upload successful for ${file.name}`)),
      catchError(error => {
        console.error(`[PhotoService] Upload failed for ${file.name}:`, error);
        throw error;
      })
    );
  }

  /**
   * Upload photos to album (batch)
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
   * Get thumbnail URL for a photo
   */
  getThumbnailUrl(photoId: string): string {
    return `${this.API_URL}/${photoId}/download?quality=Thumbnail`;
  }

  /**
   * Poll processing status repeatedly
   */
  pollProcessingStatus(photoId: string, intervalMs: number = 2000): Observable<ProcessingStatus> {
    return new Observable(observer => {
      const interval = setInterval(() => {
        this.getPhotoProcessingStatus(photoId).subscribe({
          next: (status) => observer.next(status),
          error: (error) => {
            clearInterval(interval);
            observer.error(error);
          }
        });
      }, intervalMs);

      return () => clearInterval(interval);
    });
  }

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
