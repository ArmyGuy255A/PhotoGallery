import { Locator, expect } from '@playwright/test';

/**
 * Image load assertion helpers.
 *
 * The browser's broken-image icon is rendered when `<img>` fails to load — and
 * crucially the element is still "visible" from Playwright's perspective. The
 * only reliable way to tell apart a genuinely-rendered image from a broken one
 * is to evaluate the underlying `HTMLImageElement` state in the page context
 * (`naturalWidth > 0` and `complete === true`).
 *
 * This helper is the exact gap that motivated D006: a Karma test with a mocked
 * HttpClient cannot perform this check because the `<img>` never actually
 * fetches anything.
 */

interface ImageState {
  complete: boolean;
  naturalWidth: number;
  src: string;
}

async function readImageState(img: Locator): Promise<ImageState> {
  return await img.evaluate((el: HTMLImageElement) => ({
    complete: el.complete,
    naturalWidth: el.naturalWidth,
    src: el.currentSrc || el.src
  }));
}

/**
 * Assert an `<img>` element fully loaded its source. Polls until both
 * `complete` is true and `naturalWidth > 0`, or times out.
 */
export async function assertImageLoads(img: Locator, timeoutMs = 15_000): Promise<void> {
  await expect
    .poll(async () => readImageState(img), { timeout: timeoutMs, intervals: [200, 500, 1000] })
    .toMatchObject({ complete: true, naturalWidth: expect.any(Number) });

  const state = await readImageState(img);
  expect(state.naturalWidth, `Image at ${state.src} loaded but naturalWidth is 0 (broken)`).toBeGreaterThan(0);
}

/**
 * Inverse — assert an image is in the broken state (loaded but
 * `naturalWidth === 0`). Used by the placeholder-fallback test to confirm the
 * (error) handler kicked in for the right reason.
 */
export async function assertImageBroken(img: Locator): Promise<void> {
  const state = await readImageState(img);
  expect(state.naturalWidth, `Expected ${state.src} to be broken but naturalWidth=${state.naturalWidth}`).toBe(0);
}
