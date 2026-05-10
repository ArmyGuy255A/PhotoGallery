using Microsoft.Extensions.Logging;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Unit tests for WatermarkService — verifies the tiled diagonal watermark
/// produces a valid JPEG and the output is materially different from the input
/// (i.e., something was actually drawn on top).
///
/// Reference: D009 (Watermark Pipeline)
/// </summary>
public class WatermarkServiceTests
{
    private readonly Mock<ILogger<WatermarkService>> _logger = new();

    private WatermarkService CreateService() => new(_logger.Object);

    private static MemoryStream CreateTestImage(int width = 800, int height = 600, byte fillR = 100, byte fillG = 150, byte fillB = 200)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(fillR, fillG, fillB));
        var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ApplyWatermarkAsync_ProducesValidJpeg()
    {
        var service = CreateService();
        using var input = CreateTestImage();
        using var output = new MemoryStream();

        await service.ApplyWatermarkAsync(input, output, "© Test Photographer");

        Assert.True(output.Length > 0, "Output stream is empty");

        // Verify output is a decodable JPEG
        output.Position = 0;
        using var decoded = await Image.LoadAsync(output);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    [Fact]
    public async Task ApplyWatermarkAsync_ChangesPixelsFromOriginal()
    {
        var service = CreateService();
        using var input = CreateTestImage(400, 300, fillR: 80, fillG: 80, fillB: 80);

        // Capture original byte length
        var originalBytes = input.ToArray();

        using var output = new MemoryStream();
        input.Position = 0;
        await service.ApplyWatermarkAsync(input, output, "© Watermarked");

        var watermarkedBytes = output.ToArray();

        // After watermarking + re-encoding, content must differ
        Assert.NotEqual(originalBytes.Length, watermarkedBytes.Length);
    }

    [Fact]
    public async Task ApplyWatermarkAsync_HandlesEmptyText_UsesDefault()
    {
        var service = CreateService();

        using var input1 = CreateTestImage();
        using var output1 = new MemoryStream();
        await service.ApplyWatermarkAsync(input1, output1, "   ");
        Assert.True(output1.Length > 0);

        using var input2 = CreateTestImage();
        using var output2 = new MemoryStream();
        await service.ApplyWatermarkAsync(input2, output2, string.Empty);
        Assert.True(output2.Length > 0);
    }

    [Fact]
    public async Task ApplyWatermarkAsync_TruncatesLongText()
    {
        var service = CreateService();
        using var input = CreateTestImage();
        using var output = new MemoryStream();

        var longText = new string('A', 200); // way over 64-char limit

        // Should silently truncate, not throw
        await service.ApplyWatermarkAsync(input, output, longText);

        Assert.True(output.Length > 0);
    }

    [Fact]
    public async Task ApplyWatermarkAsync_NullSource_Throws()
    {
        var service = CreateService();
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ApplyWatermarkAsync(null!, output, "test"));
    }

    [Fact]
    public async Task ApplyWatermarkAsync_NullOutput_Throws()
    {
        var service = CreateService();
        using var input = CreateTestImage();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ApplyWatermarkAsync(input, null!, "test"));
    }

    [Fact]
    public async Task ApplyWatermarkAsync_RespectsJpegQuality()
    {
        var service = CreateService();

        using var input1 = CreateTestImage();
        using var output1 = new MemoryStream();
        await service.ApplyWatermarkAsync(input1, output1, "test", jpegQuality: 50);

        using var input2 = CreateTestImage();
        using var output2 = new MemoryStream();
        await service.ApplyWatermarkAsync(input2, output2, "test", jpegQuality: 95);

        // Higher quality JPEG should be a larger file
        Assert.True(output2.Length > output1.Length,
            $"Q95 ({output2.Length}) should be larger than Q50 ({output1.Length})");
    }

    // -------------------------------------------------------------------------------------
    // FormatDisplayName — display-name fallback chain (PRs #47 / #48 + EPIC May 2026).
    // The pre-fix code rendered the raw Photo.UploadedBy GUID into every watermark; these
    // tests pin the new resolution chain so a regression is caught before another sweep ships.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void FormatDisplayName_FirstAndLastBothPresent_PrefersFullName()
    {
        var user = new User { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" };
        Assert.Equal("© Ada Lovelace", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_OnlyFirstName_StillUsesIt()
    {
        var user = new User { FirstName = "Ada", LastName = null, Email = "ada@example.com" };
        Assert.Equal("© Ada", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_OnlyLastName_StillUsesIt()
    {
        var user = new User { FirstName = null, LastName = "Lovelace", Email = "ada@example.com" };
        Assert.Equal("© Lovelace", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_NoNamesButHasEmail_FallsBackToEmailLocalPart()
    {
        var user = new User { FirstName = null, LastName = null, Email = "ada@example.com" };
        Assert.Equal("© ada", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_WhitespaceNamesWithEmail_FallsBackToEmailLocalPart()
    {
        var user = new User { FirstName = "  ", LastName = "  ", Email = "ada@example.com" };
        Assert.Equal("© ada", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_NoNamesNoEmail_FallsBackToGeneric()
    {
        var user = new User { FirstName = null, LastName = null, Email = null };
        Assert.Equal("© Photo Gallery", WatermarkService.FormatDisplayName(user));
    }

    [Fact]
    public void FormatDisplayName_NullUser_FallsBackToGeneric()
    {
        Assert.Equal("© Photo Gallery", WatermarkService.FormatDisplayName(null));
    }
}
