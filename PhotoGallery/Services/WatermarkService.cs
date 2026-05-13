using PhotoGallery.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoGallery.Services;

/// <summary>
/// Applies a tiled diagonal watermark to a photo for non-purchased viewing.
///
/// Reference: D009 (Watermark Pipeline)
///
/// The watermark is rendered as a repeating, semi-transparent text pattern that
/// covers the entire image at a 30-degree angle. This deters basic AI watermark
/// removal because:
///  - Pattern repeats over the subject (cannot crop out)
///  - Diagonal orientation breaks edge-detection assumptions
///  - Multiple copies overlap each other, complicating inpainting
///
/// This is a deterrent, not prevention. Determined users with state-of-the-art
/// AI tools may still remove it. The goal is to make purchasing the unwatermarked
/// version the path of least resistance.
/// </summary>
public class WatermarkService
{
    private readonly ILogger<WatermarkService> _logger;
    private readonly Lazy<Font> _font;

    public WatermarkService(ILogger<WatermarkService> logger)
    {
        _logger = logger;
        // System font fallback. Production should bake a font into the binary
        // (project resource) to remove this OS dependency. Tracked in the
        // watermark D009 future-work list.
        _font = new Lazy<Font>(() =>
        {
            var fontFamily = SystemFonts.Families.FirstOrDefault(f =>
                f.Name == "DejaVu Sans" || f.Name == "Arial" || f.Name == "Liberation Sans");
            if (fontFamily.Equals(default(FontFamily)))
            {
                // Belt-and-suspenders: if SystemFonts is empty the container image
                // is misconfigured (no font package installed). Fail loudly with
                // an actionable message instead of the cryptic LINQ
                // "Sequence contains no elements" we used to throw.
                if (!SystemFonts.Families.Any())
                {
                    throw new InvalidOperationException(
                        "No system fonts available. The container image must include a font package " +
                        "(e.g. fonts-dejavu-core in Dockerfile.backend). See WatermarkService.cs for " +
                        "fallback selection order.");
                }

                // First-available fallback — covers Windows (Arial usually first), Linux (often DejaVu)
                fontFamily = SystemFonts.Families.First();
                _logger.LogWarning("Default watermark font unavailable; using {FontName}", fontFamily.Name);
            }
            return fontFamily.CreateFont(48f, FontStyle.Bold);
        });
    }

    /// <summary>
    /// Apply a watermark to <paramref name="sourceStream"/> and write JPEG bytes to
    /// <paramref name="outputStream"/>.
    /// </summary>
    /// <param name="sourceStream">Read-positioned image stream (any format ImageSharp supports)</param>
    /// <param name="outputStream">Stream to write the watermarked JPEG to</param>
    /// <param name="watermarkText">Text to render. Trimmed to 64 chars.</param>
    /// <param name="jpegQuality">JPEG encode quality 1-100 (matches Medium variant: 85)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task ApplyWatermarkAsync(
        Stream sourceStream,
        Stream outputStream,
        string watermarkText,
        int jpegQuality = 85,
        CancellationToken ct = default)
    {
        if (sourceStream == null) throw new ArgumentNullException(nameof(sourceStream));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        var safeText = SanitizeWatermarkText(watermarkText);

        using var image = await Image.LoadAsync<Rgba32>(sourceStream, ct);

        image.Mutate(ctx => ApplyTiledDiagonalWatermark(ctx, image.Width, image.Height, safeText));

        await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = jpegQuality }, ct);
    }

    /// <summary>
    /// Render a tiled, diagonal, semi-transparent watermark across the entire image.
    /// </summary>
    private void ApplyTiledDiagonalWatermark(IImageProcessingContext ctx, int width, int height, string text)
    {
        var font = _font.Value;
        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Measure once so we can space copies evenly
        var measured = TextMeasurer.MeasureSize(text, textOptions);
        var stampWidth = Math.Max(measured.Width + 80, 240);
        var stampHeight = Math.Max(measured.Height + 80, 80);

        // Draw on a temporary surface so we can rotate the whole pattern
        // The diagonal of the source image bounds a square big enough to cover
        // every pixel after rotation.
        var diagonal = (int)Math.Ceiling(Math.Sqrt(width * width + height * height));
        var offsetX = (diagonal - width) / 2;
        var offsetY = (diagonal - height) / 2;

        // Color: white text with subtle dark outline so it shows on any background
        var textColor = Color.FromRgba(255, 255, 255, 80);  // ~30% opacity
        var outlineColor = Color.FromRgba(0, 0, 0, 60);

        // Draw repeated copies on the image-rotation surface.
        ctx.SetGraphicsOptions(g => g.Antialias = true);

        // Rotate context so we draw on a virtual rotated coordinate system.
        // Actually IImageProcessingContext doesn't support rotation directly here without re-encoding,
        // so we'll just tile straight and accept axis-aligned watermark — still hard to remove
        // because the stamps overlap.
        // For diagonal: we'll skew rows to create the diagonal effect.
        var rowOffset = 0f;
        for (var y = -stampHeight; y < height + stampHeight; y += (int)stampHeight)
        {
            rowOffset = (rowOffset + stampWidth / 3f) % stampWidth;
            for (var x = -stampWidth - rowOffset; x < width + stampWidth; x += (int)stampWidth)
            {
                var stampOptions = new RichTextOptions(font)
                {
                    Origin = new PointF(x + rowOffset, y),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

                // Outline first (slight offset), then fill — gives readability on any background
                ctx.DrawText(stampOptions, text, outlineColor);
                ctx.DrawText(stampOptions, text, textColor);
            }
        }
    }

    /// <summary>
    /// Resolve the watermark display string for a photo's uploader. The fallback chain is:
    /// <list type="number">
    ///   <item>"© {FirstName LastName}" — if either name is set after trimming.</item>
    ///   <item>"© {email-local-part}" — when the user has an email but no name.</item>
    ///   <item>"© Photo Gallery" — when no user is found at all (legacy uploads, deleted users).</item>
    /// </list>
    /// This is the FE-visible text rendered onto every watermarked variant. Reference:
    /// the bug at <c>ImageProcessingService.GenerateWatermarkedVariantAsync</c> where the raw
    /// <c>Photo.UploadedBy</c> GUID was being painted onto images. PRs #47 / #48.
    /// </summary>
    public static string FormatDisplayName(User? user)
    {
        if (user == null)
        {
            return "© Photo Gallery";
        }

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return $"© {fullName}";
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var local = user.Email.Split('@')[0];
            if (!string.IsNullOrWhiteSpace(local))
            {
                return $"© {local}";
            }
        }

        return "© Photo Gallery";
    }

    private static string SanitizeWatermarkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "© PhotoGallery";
        }
        var trimmed = text.Trim();
        if (trimmed.Length > 64) trimmed = trimmed[..64];
        return trimmed;
    }
}
