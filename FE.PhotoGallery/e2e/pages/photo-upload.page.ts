import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Photo upload component page object.
 *
 * Wraps the in-component upload flow: file chooser, progress list, status
 * badges. Used by the regression spec to confirm the in-component thumbnail
 * shows up (the working code path) before asserting the album-grid card also
 * shows its thumbnail (the path that was broken).
 */
export class PhotoUploadPage extends BasePage {
  readonly component: Locator;
  readonly chooseFilesButton: Locator;
  readonly fileInput: Locator;
  readonly progressList: Locator;
  readonly summary: Locator;

  constructor(page: Page) {
    super(page);
    this.component = this.byTestId('photo-upload-component');
    this.chooseFilesButton = this.byTestId('photo-upload-choose-files');
    this.fileInput = this.byTestId('photo-upload-input');
    this.progressList = this.byTestId('upload-progress-list');
    this.summary = this.byTestId('upload-summary');
  }

  /** Set files programmatically — bypasses the OS file chooser entirely. */
  async chooseFiles(absolutePaths: string[]): Promise<void> {
    await this.fileInput.setInputFiles(absolutePaths);
  }

  /** Locate a single upload-progress row by file name. */
  uploadItemByFileName(fileName: string): Locator {
    return this.page
      .locator('[data-testid="upload-item"]', {
        has: this.page.locator(`[data-testid="upload-item-filename"]`, { hasText: fileName })
      });
  }

  /**
   * Wait for an upload row to reach a terminal status (`success` or `error`).
   * The component sets `data-upload-status` on each row so we can poll without
   * scraping the user-visible status text.
   */
  async waitForUploadComplete(fileName: string, timeoutMs = 60_000): Promise<void> {
    const item = this.uploadItemByFileName(fileName);
    await expect(item).toHaveAttribute('data-upload-status', /success|error/, { timeout: timeoutMs });
  }

  /** Assert the in-component thumbnail rendered for a successfully-uploaded file. */
  async expectInComponentThumbnailVisible(fileName: string): Promise<void> {
    const thumb = this.uploadItemByFileName(fileName).locator('[data-testid="upload-item-thumbnail"]');
    await expect(thumb).toBeVisible({ timeout: 15_000 });
  }
}
