import { Component, inject, signal } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { catchError, of } from 'rxjs';
import { BackToDashboardComponent } from '../back-to-dashboard/back-to-dashboard.component';
import { environment } from '../../../environments/environment';

interface OrphanReapReport {
  scanned?: number;
  orphanedAlbums?: number;
  orphanedPhotos?: number;
  blobsDeleted?: number;
  bytesReclaimed?: number;
  skippedByGracePeriod?: number;
  elapsedMs?: number;
}

interface ReconcileReport {
  scanned?: number;
  reconciled?: number;
  errors?: number;
  elapsedMs?: number;
}

type JobState = 'idle' | 'running' | 'success' | 'error';

/**
 * Admin Settings page. Today exposes two storage-maintenance buttons that
 * call the existing admin-only POST endpoints on PhotosController:
 *   - /api/photos/admin/reap-orphans        (orphaned-blob sweep)
 *   - /api/photos/admin/reconcile-storage   (DB ↔ storage reconciliation)
 * Both endpoints return a JSON summary; the UI renders the numbers inline
 * so the admin can see exactly what landed. Disabled while a job is in
 * flight so an impatient double-click doesn't kick off two passes.
 */
@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [CommonModule, BackToDashboardComponent, DecimalPipe],
  template: `
    <section
      class="admin-settings"
      data-testid="admin-settings-page"
      aria-labelledby="admin-settings-title"
    >
      <app-back-to-dashboard></app-back-to-dashboard>
      <h1 id="admin-settings-title">Admin Settings</h1>

      <section class="admin-card" data-testid="admin-card-storage-maintenance">
        <h2>Storage Maintenance</h2>
        <p class="card-hint">
          Run a maintenance sweep against the active storage backend (Minio in
          development, Azure Blob in production). Both jobs are synchronous —
          the page will sit on the button until the server responds with the
          per-cycle summary.
        </p>

        <div class="action-row">
          <button
            type="button"
            class="btn-primary"
            data-testid="admin-reap-orphans-button"
            [disabled]="reapState() === 'running'"
            (click)="runReapOrphans()">
            <ng-container *ngIf="reapState() !== 'running'; else reapingTpl">
              🧹 Reap orphaned blobs
            </ng-container>
            <ng-template #reapingTpl>Reaping…</ng-template>
          </button>
          <span class="action-hint">
            Deletes any blob under <code>photogallery/&lt;album&gt;/&lt;photo&gt;/</code>
            whose matching DB row no longer exists.
          </span>
        </div>

        <div *ngIf="reapError() as err" class="alert alert-error" data-testid="admin-reap-orphans-error">
          {{ err }}
        </div>
        <div *ngIf="reapReport() as r" class="alert alert-success" data-testid="admin-reap-orphans-report">
          <strong>Reap complete:</strong>
          <ul class="report-list">
            <li>{{ r.scanned | number }} prefix(es) scanned</li>
            <li>{{ r.orphanedAlbums | number }} orphaned album(s)</li>
            <li>{{ r.orphanedPhotos | number }} orphaned photo(s)</li>
            <li>{{ r.blobsDeleted | number }} blob(s) deleted</li>
            <li>{{ formatBytes(r.bytesReclaimed ?? 0) }} reclaimed</li>
            <li *ngIf="r.skippedByGracePeriod">
              {{ r.skippedByGracePeriod | number }} skipped (within grace window)
            </li>
            <li>completed in {{ r.elapsedMs | number }} ms</li>
          </ul>
        </div>

        <hr class="card-divider" />

        <div class="action-row">
          <button
            type="button"
            class="btn-secondary"
            data-testid="admin-reconcile-storage-button"
            [disabled]="reconcileState() === 'running'"
            (click)="runReconcile()">
            <ng-container *ngIf="reconcileState() !== 'running'; else reconcilingTpl">
              ⚖️ Reconcile storage ↔ database
            </ng-container>
            <ng-template #reconcilingTpl>Reconciling…</ng-template>
          </button>
          <span class="action-hint">
            Walks DB rows that point at storage and verifies each blob is
            actually present; logs anything broken.
          </span>
        </div>

        <div *ngIf="reconcileError() as err" class="alert alert-error" data-testid="admin-reconcile-error">
          {{ err }}
        </div>
        <div *ngIf="reconcileReport() as r" class="alert alert-success" data-testid="admin-reconcile-report">
          <strong>Reconciliation complete:</strong>
          <ul class="report-list">
            <li>{{ r.scanned | number }} item(s) scanned</li>
            <li>{{ r.reconciled | number }} reconciled</li>
            <li *ngIf="r.errors">{{ r.errors | number }} error(s)</li>
            <li>completed in {{ r.elapsedMs | number }} ms</li>
          </ul>
        </div>
      </section>

      <section class="coming-soon">
        <p class="hint">
          More admin tools (user management, role assignment, site-wide settings)
          will land alongside issue #70.
        </p>
      </section>
    </section>
  `,
  styles: [`
    .admin-settings {
      padding: 24px;
      max-width: 1200px;
      margin: 0 auto;
    }
    h1 {
      margin: 8px 0 12px;
      font-size: 24px;
    }
    .admin-card {
      background: #ffffff;
      border: 1px solid #e0e6ed;
      border-radius: 12px;
      padding: 20px;
      margin: 16px 0;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }
    .admin-card h2 {
      margin: 0 0 6px;
      font-size: 18px;
      color: #1f2937;
    }
    .card-hint {
      color: #6b7280;
      font-size: 13px;
      margin: 0 0 16px;
    }
    .card-divider {
      border: none;
      border-top: 1px solid #f0f1f3;
      margin: 20px 0;
    }
    .action-row {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .action-hint {
      color: #6b7280;
      font-size: 12px;
      max-width: 520px;
    }
    .action-hint code {
      background: #f4f5f7;
      padding: 1px 5px;
      border-radius: 4px;
      font-size: 11px;
    }
    .btn-primary, .btn-secondary {
      padding: 8px 16px;
      border-radius: 6px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      border: 1px solid transparent;
      transition: background 0.15s ease;
    }
    .btn-primary {
      background: #0d6efd;
      color: white;
    }
    .btn-primary:hover:not(:disabled) { background: #0b5ed7; }
    .btn-secondary {
      background: #ffffff;
      color: #1f2937;
      border-color: #d1d5db;
    }
    .btn-secondary:hover:not(:disabled) { background: #f9fafb; }
    .btn-primary:disabled, .btn-secondary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .alert {
      padding: 10px 14px;
      border-radius: 8px;
      margin-top: 12px;
      font-size: 13px;
    }
    .alert-error {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
    }
    .alert-success {
      background: #ecfdf5;
      border: 1px solid #a7f3d0;
      color: #065f46;
    }
    .report-list {
      margin: 6px 0 0;
      padding-left: 20px;
    }
    .coming-soon {
      margin-top: 16px;
    }
    .coming-soon .hint {
      font-size: 13px;
      color: #888;
    }
  `]
})
export class AdminSettingsComponent {
  private readonly http = inject(HttpClient);

  readonly reapState = signal<JobState>('idle');
  readonly reapReport = signal<OrphanReapReport | null>(null);
  readonly reapError = signal<string | null>(null);

  readonly reconcileState = signal<JobState>('idle');
  readonly reconcileReport = signal<ReconcileReport | null>(null);
  readonly reconcileError = signal<string | null>(null);

  runReapOrphans(): void {
    this.reapState.set('running');
    this.reapReport.set(null);
    this.reapError.set(null);
    this.http.post<OrphanReapReport>(`${environment.apiUrl}/api/photos/admin/reap-orphans`, {})
      .pipe(catchError(err => {
        this.reapState.set('error');
        this.reapError.set(this.extractErrorMessage(err));
        return of(null);
      }))
      .subscribe(report => {
        if (!report) return;
        this.reapState.set('success');
        this.reapReport.set(report);
      });
  }

  runReconcile(): void {
    this.reconcileState.set('running');
    this.reconcileReport.set(null);
    this.reconcileError.set(null);
    this.http.post<ReconcileReport>(`${environment.apiUrl}/api/photos/admin/reconcile-storage`, {})
      .pipe(catchError(err => {
        this.reconcileState.set('error');
        this.reconcileError.set(this.extractErrorMessage(err));
        return of(null);
      }))
      .subscribe(report => {
        if (!report) return;
        this.reconcileState.set('success');
        this.reconcileReport.set(report);
      });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  }

  private extractErrorMessage(err: unknown): string {
    const e = err as { error?: { message?: string }; status?: number; message?: string };
    if (e?.error?.message) return e.error.message;
    if (e?.status === 403) return 'Forbidden — this page requires an admin account.';
    if (e?.status === 401) return 'Unauthorized — please sign in as an admin.';
    return e?.message || 'Request failed';
  }
}
