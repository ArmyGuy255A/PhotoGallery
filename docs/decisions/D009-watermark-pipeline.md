# ADR-009: Watermark Pipeline — Reuse Existing SixLabors.ImageSharp

**Status:** Accepted  
**Date:** 2026-05-09  
**Context:** EPIC-01 / B3 — server-side watermark pipeline for paid downloads

## Problem

Phase 17+ paid-photo feature requires a server-side watermark applied to the Medium quality variant for non-purchased viewing. Watermark must:

1. Be hard to remove via simple AI tools (overlap subject; tiled or repeating pattern)
2. Run inside Linux Docker containers without OS dependencies
3. Be applied during photo processing (no per-request rendering)

## Decision Drivers

The project **already uses `SixLabors.ImageSharp` v3.1.12** for resize/encode operations (`ImageProcessingService`). We have two viable paths:

1. **Reuse ImageSharp** for watermarking (consistent with existing pipeline)
2. **Add Magick.NET-Q8-AnyCPU** alongside (broader format support, different license)

## Decision

**Reuse SixLabors.ImageSharp v3.x** for watermarking.

### Rationale
- **DRY**: existing `ImageProcessingService` already opens, resizes, and encodes images via ImageSharp. Adding watermarking is one more `Mutate()` call in the same pipeline.
- **No new dependencies**: keeps Docker images smaller, no native binaries to debug
- **Watermarking API is sufficient**: `DrawText`, `DrawImage`, opacity, rotation, tiling all supported
- **Architect skill compliance**: extending an established pattern beats introducing a parallel one

### License Note (Flagged for Separate Decision)

ImageSharp v3.x uses the **Six Labors Split License** — free for non-commercial use, commercial license required (~$3K/year) for monetized applications. **This commitment was made when v3.1.12 was added to the project for the existing image processing.** This ADR does not change that commitment.

The commercial-license question affects the entire image processing pipeline, not just watermarking. **It should be resolved separately**, ideally before EPIC-04 (payment processing) launches the app commercially. Options when revisiting:
- Stay on v3 + buy commercial license (~$3K/year)
- Downgrade to v2.x (Apache 2.0, free, but no future updates)
- Migrate to Magick.NET-Q8-AnyCPU (Apache 2.0, free, but full pipeline rewrite)

This is captured as a known issue, not a blocker for B3.

## Consequences

### Immediate
- Create `WatermarkService` using `SixLabors.ImageSharp.Drawing` (sub-package for text drawing)
- Add `SixLabors.ImageSharp.Drawing` NuGet package (companion to ImageSharp; same license)
- Create the watermarked variant alongside existing 4 quality versions
- Bake a default font into the binary (project resources) to avoid OS font dependencies

### Watermark Strategy
- **Pattern**: tiled diagonal text covering the entire image (not just a corner)
- **Opacity**: 30% so the image is still viewable but watermark is unmistakable
- **Rotation**: 30 degrees from horizontal
- **Color**: white with subtle black outline for visibility on any background
- **Default text**: photographer's display name + `© watermark`. Per-photographer customization is a future EPIC.

### File Naming
- New storage key suffix: `medium-watermarked.jpg`
- Public/guest endpoints serve the watermarked URL via short-lived pre-signed URL
- Cart-checkout zip extracts the unwatermarked Medium (quality from cart selection)

## Future Work
- Custom watermark per photographer (logo upload, text customization)
- Format expansion (RAW, HEIC) — would justify Magick.NET migration
- Resolve commercial-license question in standalone ADR before app monetization

## Update — Thumbnails are now watermarked alongside Medium (PR #48)

The watermark pipeline initially produced only `medium-watermarked.jpg`. PR #48 extended it to also emit `thumbnail-watermarked.jpg`, applying the same tiled-diagonal pattern at thumbnail resolution. A one-shot backfill produced the new variant for every existing photo so guest previews on album list pages no longer leak un-watermarked thumbnails. No other changes to the pipeline; the watermark service, font, opacity, and rotation are unchanged. See [STORAGE_LAYER.md — Storage Path Structure](../../Documentation/Architecture/STORAGE_LAYER.md#storage-path-structure) for the updated layout.
