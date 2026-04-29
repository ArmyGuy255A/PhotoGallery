import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-albums-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="create-album-container">
      <div class="card">
        <div class="card-header">
          <h1>Create New Album</h1>
          <p class="subtitle">Create a new album to organize and share your photos</p>
        </div>

        <form [formGroup]="albumForm" (ngSubmit)="onSubmit()" class="form">
          <div class="form-group">
            <label for="title">Album Title *</label>
            <input
              id="title"
              type="text"
              formControlName="title"
              placeholder="e.g., Summer Vacation 2024"
              class="form-input"
              [class.error]="isFieldInvalid('title')"
            />
            <span class="error-message" *ngIf="isFieldInvalid('title')">
              Album title is required and must be at least 3 characters
            </span>
          </div>

          <div class="form-group">
            <label for="description">Description</label>
            <textarea
              id="description"
              formControlName="description"
              placeholder="Optional: Add a description for this album"
              class="form-textarea"
              rows="4"
            ></textarea>
          </div>

          <div class="form-actions">
            <button type="button" (click)="onCancel()" class="btn btn-secondary">
              Cancel
            </button>
            <button type="submit" class="btn btn-primary" [disabled]="albumForm.invalid || isLoading">
              <span *ngIf="!isLoading">Create Album</span>
              <span *ngIf="isLoading">Creating...</span>
            </button>
          </div>

          <div class="error-alert" *ngIf="errorMessage">
            <strong>Error:</strong> {{ errorMessage }}
          </div>

          <div class="success-alert" *ngIf="successMessage">
            <strong>Success!</strong> {{ successMessage }}
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .create-album-container {
      min-height: 100vh;
      background: #f5f7fa;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
    }

    .card {
      background: white;
      border-radius: 8px;
      box-shadow: 0 2px 16px rgba(0, 0, 0, 0.1);
      max-width: 500px;
      width: 100%;
      padding: 32px;
    }

    .card-header {
      margin-bottom: 24px;
      text-align: center;
    }

    .card-header h1 {
      margin: 0 0 8px 0;
      font-size: 24px;
      color: #333;
    }

    .subtitle {
      margin: 0;
      color: #999;
      font-size: 14px;
    }

    .form {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-weight: 500;
      color: #333;
      font-size: 14px;
    }

    .form-input,
    .form-textarea {
      padding: 10px 12px;
      border: 1px solid #ddd;
      border-radius: 4px;
      font-size: 14px;
      font-family: inherit;
      transition: border-color 0.3s;
    }

    .form-input:focus,
    .form-textarea:focus {
      outline: none;
      border-color: #3498db;
      box-shadow: 0 0 0 3px rgba(52, 152, 219, 0.1);
    }

    .form-input.error,
    .form-textarea.error {
      border-color: #e74c3c;
    }

    .error-message {
      font-size: 12px;
      color: #e74c3c;
    }

    .form-actions {
      display: flex;
      gap: 12px;
      margin-top: 12px;
    }

    .btn {
      flex: 1;
      padding: 10px 20px;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      transition: background 0.3s;
    }

    .btn-primary {
      background: #3498db;
      color: white;
    }

    .btn-primary:hover:not(:disabled) {
      background: #2980b9;
    }

    .btn-primary:disabled {
      background: #bdc3c7;
      cursor: not-allowed;
    }

    .btn-secondary {
      background: #ecf0f1;
      color: #333;
    }

    .btn-secondary:hover {
      background: #d5dbdb;
    }

    .error-alert {
      padding: 12px;
      background: #fadbd8;
      color: #922b21;
      border-left: 4px solid #e74c3c;
      border-radius: 4px;
      font-size: 14px;
    }

    .success-alert {
      padding: 12px;
      background: #d5f4e6;
      color: #186a3b;
      border-left: 4px solid #27ae60;
      border-radius: 4px;
      font-size: 14px;
    }
  `]
})
export class AlbumsCreateComponent implements OnInit {
  albumForm!: FormGroup;
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private router: Router
  ) {
    this.albumForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      description: ['']
    });
  }

  ngOnInit(): void {}

  isFieldInvalid(fieldName: string): boolean {
    const field = this.albumForm.get(fieldName);
    return !!(field && field.invalid && (field.dirty || field.touched));
  }

  onSubmit(): void {
    if (this.albumForm.invalid) {
      this.errorMessage = 'Please fill in all required fields';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    const formData = this.albumForm.value;

    const apiUrl = environment.apiUrl || '';
    const endpoint = `${apiUrl}/api/albums`;

    console.log('Album creation form submit:', {
      endpoint,
      formData,
      apiUrl
    });

    this.http.post(endpoint, formData).subscribe({
      next: (response: any) => {
        this.isLoading = false;
        this.successMessage = 'Album created successfully!';
        console.log('Album created successfully:', response);
        
        // Navigate to album detail or back to dashboard after 1 second
        setTimeout(() => {
          this.router.navigate(['/dashboard']);
        }, 1000);
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Error creating album:', error);
        const errorMsg = error?.error?.message || error?.error || error?.message || 'Failed to create album. Please try again.';
        this.errorMessage = String(errorMsg);
      }
    });
  }

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }
}
