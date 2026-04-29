import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Photo {
  id: string;
  fileName: string;
  uploadDate: Date;
  uploadedBy: string;
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

/**
 * Service for managing photos
 */
@Injectable({
  providedIn: 'root'
})
export class PhotoService {
  private readonly API_URL = '/api/photos';

  constructor(private http: HttpClient) {}

  /**
   * Upload photos to album
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
   * Get processing status for a photo
   */
  getProcessingStatus(jobId: string): Observable<any> {
    return this.http.get<any>(
      `${this.API_URL}/processing-status/${jobId}`
    );
  }

  /**
   * Download photo via access code
   */
  downloadPhotoByCode(code: string, photoId: string, quality: string = 'medium'): Observable<Blob> {
    return this.http.get(
      `/api/code/${code}/photo/${photoId}/download?quality=${quality}`,
      { responseType: 'blob' }
    );
  }

  /**
   * Get photos via access code
   */
  getPhotosByCode(code: string): Observable<Photo[]> {
    return this.http.get<Photo[]>(`/api/code/${code}/photos`);
  }
}
