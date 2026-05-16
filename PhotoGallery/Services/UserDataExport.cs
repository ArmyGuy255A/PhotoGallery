namespace PhotoGallery.Services;

/// <summary>
/// Top-level GDPR data-export envelope returned by GET /api/account/me/export.
/// schemaVersion lets future readers decide how to interpret older exports.
/// </summary>
public record UserDataExport
{
    public DateTime ExportTimestamp { get; init; } = DateTime.UtcNow;
    public string SchemaVersion { get; init; } = "1.0";
    public ProfileExport Profile { get; init; } = new();
    public List<AlbumExport> OwnedAlbums { get; init; } = new();
    public List<PhotoExport> UploadedPhotos { get; init; } = new();
    public List<AccessCodeExport> SavedAccessCodes { get; init; } = new();
    public List<DownloadExport> Downloads { get; init; } = new();
}

public record ProfileExport
{
    public string Id { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime CreatedDate { get; init; }
    public bool IsActive { get; init; }
}

public record AlbumExport
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime CreatedDate { get; init; }
    public int PhotoCount { get; init; }
}

public record PhotoExport
{
    public Guid Id { get; init; }
    public Guid AlbumId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string StorageKey { get; init; } = string.Empty;
    public DateTime UploadDate { get; init; }
    public string? Metadata { get; init; }
}

public record AccessCodeExport
{
    public Guid Id { get; init; }
    public Guid? AlbumId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime CreatedDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
}

public record DownloadExport
{
    public Guid Id { get; init; }
    public Guid PhotoId { get; init; }
    public Guid? AccessCodeId { get; init; }
    public string Quality { get; init; } = string.Empty;
    public DateTime DownloadedAt { get; init; }
}
