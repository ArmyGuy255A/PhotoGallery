import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Album {
  id: string;
  title: string;
  description: string;
  ownerId: string;
  createdDate: Date;
  createdBy: string;
  canManage: boolean;
}

export interface CreateAlbumRequest {
  title: string;
  description: string;
}

export interface UpdateAlbumRequest {
  title?: string;
  description?: string;
}

/**
 * Service for managing albums
 */
@Injectable({
  providedIn: 'root'
})
export class AlbumService {
  private readonly API_URL = `${environment.apiUrl}/api/albums`;

  constructor(private http: HttpClient) {}

  /**
   * Get all albums
   */
  getAlbums(): Observable<Album[]> {
    return this.http.get<Album[]>(this.API_URL);
  }

  /**
   * Get album by ID
   */
  getAlbumById(id: string): Observable<Album> {
    return this.http.get<Album>(`${this.API_URL}/${id}`);
  }

  /**
   * Create new album
   */
  createAlbum(request: CreateAlbumRequest): Observable<Album> {
    return this.http.post<Album>(this.API_URL, request);
  }

  /**
   * Update album
   */
  updateAlbum(id: string, request: UpdateAlbumRequest): Observable<Album> {
    return this.http.put<Album>(`${this.API_URL}/${id}`, request);
  }

  /**
   * Delete album
   */
  deleteAlbum(id: string): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${id}`);
  }

  /**
   * Get photos in album
   */
  getAlbumPhotos(albumId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/${albumId}/photos`);
  }

  /**
   * Get access codes for album
   */
  getAccessCodes(albumId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/${albumId}/access-codes`);
  }

  /**
   * Create access code
   */
  createAccessCode(albumId: string, expiresForever: boolean = false, expirationDays: number = 30): Observable<any> {
    return this.http.post<any>(
      `${this.API_URL}/${albumId}/access-codes`,
      { expiresForever, expirationDays }
    );
  }

  /**
   * Delete access code
   */
  deleteAccessCode(albumId: string, codeId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.API_URL}/${albumId}/access-codes/${codeId}`
    );
  }
}
