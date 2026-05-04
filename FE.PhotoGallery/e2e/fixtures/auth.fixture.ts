import { APIRequestContext } from '@playwright/test';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';
const DEV_TOKEN = process.env['DEV_BEARER_TOKEN'] ?? 'test-token';

/**
 * Auth helpers for E2E specs.
 *
 * In DISABLE_AUTH=true mode (the local dev path) the backend accepts any
 * bearer token and resolves it to the `testadmin@localhost` admin user. Specs
 * that need to hit admin endpoints can use {@link adminAuthHeaders}; specs
 * that need to create test fixtures via the API can use
 * {@link createAlbumViaApi} which is faster than driving the UI for setup.
 */
export function adminAuthHeaders(): Record<string, string> {
  return { Authorization: `Bearer ${DEV_TOKEN}` };
}

export interface CreatedAlbum {
  id: string;
  title: string;
}

/**
 * Create an album via the admin API. Returns the album id so the spec can
 * navigate directly to /albums/{id} without driving the create-album UI.
 */
export async function createAlbumViaApi(
  request: APIRequestContext,
  title: string,
  description = 'E2E-created album'
): Promise<CreatedAlbum> {
  const response = await request.post(`${BACKEND_BASE_URL}/api/albums`, {
    headers: { 'Content-Type': 'application/json', ...adminAuthHeaders() },
    data: { title, description }
  });
  if (!response.ok()) {
    throw new Error(`createAlbumViaApi failed: ${response.status()} ${await response.text()}`);
  }
  const body = (await response.json()) as { id: string; title: string };
  return { id: body.id, title: body.title };
}
