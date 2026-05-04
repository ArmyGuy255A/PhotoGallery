import * as fs from 'fs';
import * as path from 'path';

const SAMPLE_PHOTOS_DIR = process.env['SAMPLE_PHOTOS_DIR'] ?? path.resolve(__dirname, '../../../SamplePhotos');

/**
 * Returns absolute paths to a slice of the local SamplePhotos/ folder. Used
 * by upload specs so they have real JPEGs to push through the upload flow.
 *
 * Defaults to the SamplePhotos folder at the repo root, but honors
 * SAMPLE_PHOTOS_DIR for CI environments that mount sample data elsewhere.
 */
export function getSamplePhotos(count = 1): string[] {
  if (!fs.existsSync(SAMPLE_PHOTOS_DIR)) {
    throw new Error(`SamplePhotos directory not found at ${SAMPLE_PHOTOS_DIR}. Set SAMPLE_PHOTOS_DIR to override.`);
  }
  const all = fs
    .readdirSync(SAMPLE_PHOTOS_DIR)
    .filter(f => /\.(jpe?g|png)$/i.test(f))
    .map(f => path.join(SAMPLE_PHOTOS_DIR, f));
  if (all.length === 0) {
    throw new Error(`No image files found in ${SAMPLE_PHOTOS_DIR}.`);
  }
  return all.slice(0, count);
}
