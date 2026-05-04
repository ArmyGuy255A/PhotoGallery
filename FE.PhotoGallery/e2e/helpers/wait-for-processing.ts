import { APIRequestContext, expect } from '@playwright/test';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

interface ProcessingStatus {
  photoId: string;
  status: string;
  completedVersions: number;
  totalVersions: number;
  percentComplete: number;
  hasThumbnail: boolean;
  hasLow: boolean;
  hasMedium: boolean;
  hasHigh: boolean;
}

/**
 * Poll `/api/photos/{id}/status` until processing reaches 100% (all four
 * quality versions present) or the timeout elapses.
 *
 * Used after upload to wait for `PhotoProcessingWorker` to finish before the
 * test asserts the album grid renders thumbnails — without this, the test is
 * a coin flip on processing speed.
 */
export async function waitForPhotoProcessing(
  request: APIRequestContext,
  photoId: string,
  options: { timeoutMs?: number; intervalMs?: number; bearerToken?: string } = {}
): Promise<ProcessingStatus> {
  const { timeoutMs = 60_000, intervalMs = 1_000, bearerToken } = options;
  const headers: Record<string, string> = bearerToken ? { Authorization: `Bearer ${bearerToken}` } : {};
  const startedAt = Date.now();

  let lastStatus: ProcessingStatus | null = null;
  while (Date.now() - startedAt < timeoutMs) {
    const response = await request.get(`${BACKEND_BASE_URL}/api/photos/${photoId}/status`, { headers });
    if (response.ok()) {
      lastStatus = (await response.json()) as ProcessingStatus;
      if (lastStatus.percentComplete === 100) {
        return lastStatus;
      }
    }
    await new Promise(resolve => setTimeout(resolve, intervalMs));
  }

  expect(
    lastStatus,
    `Photo ${photoId} did not finish processing within ${timeoutMs}ms; last status: ${JSON.stringify(lastStatus)}`
  ).not.toBeNull();
  expect(lastStatus!.percentComplete, `Photo ${photoId} stalled at ${lastStatus!.percentComplete}%`).toBe(100);
  return lastStatus!;
}
