using PhotoGallery.Enums;

namespace PhotoGallery.Services;

/// <summary>
/// Streams a ZIP archive of pre-authorised cart items to an output stream.
///
/// Both the access-code cart-checkout flow (<c>AccessCodeController.DownloadCart</c>)
/// and the authenticated per-user cart-checkout flow (<c>CartController.Download</c>)
/// consume this service. The service is dumb on purpose: the caller has already
/// authorised every item it passes in and resolved the album each photo belongs to.
/// This keeps quality-resolution, watermark-stripping (handled by the storage-key
/// builder), and filename conventions identical across both flows.
/// </summary>
public interface ICartZipService
{
    /// <summary>Maximum cart size kept in lock-step with the frontend constant.</summary>
    int MaxItemsPerCart { get; }

    /// <summary>
    /// Stream a ZIP archive of <paramref name="items"/> to <paramref name="output"/>.
    /// Each item that is successfully added is logged to the <c>Download</c> table
    /// (with <paramref name="accessCodeId"/> nullable for authenticated-user flows).
    /// Items whose storage object is missing, or which fail to stream, are silently
    /// skipped (logged at warning) — partial ZIPs are better than no ZIP.
    /// </summary>
    /// <returns>The number of items successfully written into the archive.</returns>
    Task<int> StreamCartZipAsync(
        IReadOnlyList<CartZipItem> items,
        Stream output,
        Guid? accessCodeId,
        string? remoteIp);
}

/// <summary>
/// A single, pre-authorised entry to include in a cart-download ZIP.
/// The caller is responsible for confirming the user is allowed to download
/// this photo at this quality before constructing the item.
/// </summary>
public class CartZipItem
{
    public Guid PhotoId { get; set; }

    /// <summary>Album the photo belongs to (used to derive the storage key).</summary>
    public Guid AlbumId { get; set; }

    /// <summary>Display filename used inside the ZIP (sanitised by the service).</summary>
    public string FileName { get; set; } = string.Empty;

    public QualityType Quality { get; set; }
}
