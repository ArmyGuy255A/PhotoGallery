import { Component, EventEmitter, Input, Output, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

/**
 * Form component for creating an access code with calendar-based expiration.
 *
 * Usage:
 *   <app-access-code-form [albumId]="albumId" (codeCreated)="onCreated()" />
 */
@Component({
  selector: 'app-access-code-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="access-code-form">
      <h3 class="form-title">Generate Access Code</h3>

      <fieldset class="expiration-fieldset" [disabled]="isSubmitting">
        <legend>Expiration</legend>

        <label class="radio-option">
          <input type="radio" name="expirationMode" value="date" [(ngModel)]="expirationMode">
          <span>Expires on a specific date</span>
        </label>

        <div class="date-picker-row" *ngIf="expirationMode === 'date'">
          <label for="expirationDate">Expiration date:</label>
          <input
            type="date"
            id="expirationDate"
            class="date-input"
            [(ngModel)]="expirationDateStr"
            [min]="minDateStr"
            required>
        </div>

        <label class="radio-option">
          <input type="radio" name="expirationMode" value="forever" [(ngModel)]="expirationMode">
          <span>Never expires</span>
        </label>
      </fieldset>

      <div class="form-actions">
        <button type="button" class="cancel-btn" (click)="onCancel()" [disabled]="isSubmitting">
          Cancel
        </button>
        <button type="button" class="submit-btn" (click)="onSubmit()" [disabled]="isSubmitting || !isValid()">
          {{ isSubmitting ? 'Generating...' : 'Generate Code' }}
        </button>
      </div>

      <div class="error-message" *ngIf="errorMessage">{{ errorMessage }}</div>
    </div>
  `,
  styles: [`
    .access-code-form {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 20px;
      margin-bottom: 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
    }

    .form-title {
      margin: 0 0 16px 0;
      font-size: 18px;
      color: #333;
    }

    .expiration-fieldset {
      border: 1px solid #e0e0e0;
      border-radius: 6px;
      padding: 16px;
      margin: 0 0 16px 0;
    }

    .expiration-fieldset legend {
      padding: 0 8px;
      font-size: 14px;
      font-weight: 600;
      color: #666;
    }

    .radio-option {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 0;
      cursor: pointer;
      font-size: 14px;
      color: #333;
    }

    .radio-option input[type="radio"] {
      cursor: pointer;
    }

    .date-picker-row {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 8px 0 8px 24px;
    }

    .date-picker-row label {
      font-size: 13px;
      color: #666;
    }

    .date-input {
      padding: 6px 10px;
      border: 1px solid #ccc;
      border-radius: 4px;
      font-size: 14px;
      font-family: inherit;
    }

    .date-input:focus {
      outline: none;
      border-color: #0066cc;
      box-shadow: 0 0 0 2px rgba(0, 102, 204, 0.1);
    }

    .form-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
    }

    .cancel-btn,
    .submit-btn {
      padding: 8px 18px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      border: none;
    }

    .cancel-btn {
      background: #f5f5f5;
      color: #333;
      border: 1px solid #ddd;
    }

    .cancel-btn:hover:not(:disabled) {
      background: #e8e8e8;
    }

    .submit-btn {
      background: #0066cc;
      color: white;
    }

    .submit-btn:hover:not(:disabled) {
      background: #0052a3;
    }

    .submit-btn:disabled,
    .cancel-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .error-message {
      margin-top: 12px;
      padding: 10px 12px;
      background: #ffebee;
      color: #c62828;
      border-radius: 4px;
      font-size: 13px;
      border-left: 4px solid #c62828;
    }
  `]
})
export class AccessCodeFormComponent implements OnInit {
  @Input() albumId!: string;
  @Output() codeCreated = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  expirationMode: 'date' | 'forever' = 'date';
  expirationDateStr = '';
  minDateStr = '';
  isSubmitting = false;
  errorMessage = '';

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    const today = new Date();
    const defaultExpiration = new Date();
    defaultExpiration.setDate(today.getDate() + 30);

    // Use date-only ISO strings for HTML5 date input
    this.minDateStr = this.toDateString(today);
    this.expirationDateStr = this.toDateString(defaultExpiration);
  }

  isValid(): boolean {
    if (this.expirationMode === 'forever') return true;
    if (!this.expirationDateStr) return false;
    const selectedDate = new Date(this.expirationDateStr);
    return selectedDate > new Date();
  }

  onSubmit(): void {
    if (!this.isValid()) {
      this.errorMessage = 'Please select a future expiration date.';
      return;
    }

    this.isSubmitting = true;
    this.errorMessage = '';

    const apiUrl = environment.apiUrl || '';
    const body = this.expirationMode === 'forever'
      ? { expiresForever: true }
      : { expiresForever: false, expirationDate: new Date(this.expirationDateStr).toISOString() };

    this.http.post(`${apiUrl}/api/albums/${this.albumId}/access-codes`, body).subscribe({
      next: () => {
        this.isSubmitting = false;
        this.codeCreated.emit();
      },
      error: (error) => {
        this.isSubmitting = false;
        this.errorMessage = error?.error || 'Failed to create access code. Please try again.';
        console.error('Error creating access code:', error);
      }
    });
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  private toDateString(date: Date): string {
    // Format as YYYY-MM-DD for HTML5 date input
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
