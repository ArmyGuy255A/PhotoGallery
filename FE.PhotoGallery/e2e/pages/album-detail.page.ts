import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';
import { assertImageLoads, assertImageBroken } from '../helpers/assert-image-loads';

/**
 * Album detail page object.
 *
 * Owns interactions with the photo grid below the upload control: counting
 * cards, locating individual photos by id, and asserting that every card's
 * `<img>` actually rendered (vs. the broken-image icon — which was the bug
 * that motivated D006/D007/D008).
 *
 * Stable selectors come from the `data-testid` attributes added to
 * `album-detail.component.ts`.
 */
export class AlbumDetailPage extends BasePage {
  readonly title: Locator;
  readonly photosCount: Locator;
  readonly photosGrid: Locator;
  readonly emptyState: Locator;

  constructor(page: Page) {
    super(page);
    this.title = this.byTestId('album-title');
    this.photosCount = this.byTestId('photos-count');
    this.photosGrid = this.byTestId('photos-grid');
    this.emptyState = this.byTestId('album-empty-photos');
  }

  /** Navigate directly to a specific album's detail page. */
  async gotoAlbum(albumId: string): Promise<void> {
    await this.goto(`/albums/${albumId}`);
    await expect(this.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });
  }

  /** Locate a single photo card by its photo id. */
  cardByPhotoId(photoId: string): Locator {
    return this.page.locator(`[data-testid="photo-card"][data-photo-id="${photoId}"]`);
  }

  /** Locate the `<img>` inside a specific photo card. */
  cardImageByPhotoId(photoId: string): Locator {
    return this.cardByPhotoId(photoId).locator('[data-testid="photo-card-image"]');
  }

  /**
   * Locate the SVG placeholder that the component falls back to when the
   * thumbnail can't load (the (error) handler clears `thumbnailUrl`).
   */
  cardPlaceholderByPhotoId(photoId: string): Locator {
    return this.cardByPhotoId(photoId).locator('[data-testid="photo-card-placeholder"]');
  }

  /** Count of rendered photo cards in the grid. */
  async cardCount(): Promise<number> {
    return await this.byTestId('photo-card').count();
  }

  /**
   * The big assertion. Verify the card for `photoId` is rendering its
   * thumbnail successfully — i.e. the bug we're fixing does NOT reproduce.
   *
   * Uses `naturalWidth > 0 && complete === true` which is the only reliable
   * way to detect "the browser actually decoded this image" — `<img onerror>`
   * fires _before_ Playwright sees DOM updates, so we check the underlying
   * image state directly.
   */
  async expectCardThumbnailLoaded(photoId: string): Promise<void> {
    const img = this.cardImageByPhotoId(photoId);
    await expect(img).toBeVisible({ timeout: 15_000 });
    await assertImageLoads(img);
  }

  /**
   * Inverse assertion — verify the card showed the SVG placeholder rather
   * than the broken-image icon. Used by the negative test that proves the
   * (error) handler is wired correctly.
   */
  async expectCardThumbnailFellBackToPlaceholder(photoId: string): Promise<void> {
    await expect(this.cardPlaceholderByPhotoId(photoId)).toBeVisible({ timeout: 15_000 });
    const img = this.cardImageByPhotoId(photoId);
    if (await img.count() > 0) {
      await assertImageBroken(img);
    }
  }
}
