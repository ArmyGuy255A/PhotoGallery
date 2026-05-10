import { Client as MinioClient } from 'minio';

/**
 * Test-only MinIO admin helper.
 *
 * Used by the watermark-thumbnail-backfill spec to delete a specific MinIO
 * object (e.g. `thumbnail-watermarked.jpg`) so we can prove that
 * `PhotoVersionUrlService.GenerateShortLivedUrlAsync` self-heals on the next
 * read. The backend does not currently expose a delete-object admin endpoint,
 * so we go directly at MinIO with the dev credentials.
 *
 * Credentials default to the development docker-compose values
 * (minioadmin/minioadmin, bucket `photogallery`, host `localhost:9000`). All
 * are overridable via environment variables for CI.
 *
 * NOTE: this helper is intentionally test-scoped — production code never
 * deletes processed variants. Do not import from app code.
 */

interface MinioAdminConfig {
  endPoint: string;
  port: number;
  useSSL: boolean;
  accessKey: string;
  secretKey: string;
  bucket: string;
}

function loadConfig(): MinioAdminConfig {
  const endpoint = process.env['MINIO_ENDPOINT'] ?? 'localhost:9000';
  const [host, portStr] = endpoint.split(':');
  return {
    endPoint: host,
    port: portStr ? Number.parseInt(portStr, 10) : 9000,
    useSSL: (process.env['MINIO_USE_SSL'] ?? 'false').toLowerCase() === 'true',
    accessKey: process.env['MINIO_ACCESS_KEY'] ?? 'minioadmin',
    secretKey: process.env['MINIO_SECRET_KEY'] ?? 'minioadmin-password',
    bucket: process.env['MINIO_BUCKET'] ?? 'photogallery'
  };
}

function client(cfg: MinioAdminConfig = loadConfig()): { client: MinioClient; bucket: string } {
  const c = new MinioClient({
    endPoint: cfg.endPoint,
    port: cfg.port,
    useSSL: cfg.useSSL,
    accessKey: cfg.accessKey,
    secretKey: cfg.secretKey
  });
  return { client: c, bucket: cfg.bucket };
}

/** Delete a single object key from the configured bucket. No-op-safe if absent. */
export async function deleteObject(objectKey: string): Promise<void> {
  const { client: c, bucket } = client();
  await c.removeObject(bucket, objectKey);
}

/** Returns true if the object exists in the bucket. */
export async function objectExists(objectKey: string): Promise<boolean> {
  const { client: c, bucket } = client();
  try {
    await c.statObject(bucket, objectKey);
    return true;
  } catch {
    return false;
  }
}

/** Compose the canonical object key for a processed photo variant.
 *
 * Note the seemingly-redundant `photogallery/` prefix: the MinIO bucket is
 * itself named `photogallery`, AND production code prefixes every object
 * within that bucket with `photogallery/...`. See
 * `PhotoVersionUrlService.BuildStorageKey`. We mirror that here exactly so
 * delete/stat target the same key the application reads/writes. */
export function variantKey(albumId: string, photoId: string, variantFileName: string): string {
  const prefix = process.env['MINIO_KEY_PREFIX'] ?? 'photogallery/';
  return `${prefix}${albumId}/${photoId}/${variantFileName}`;
}
