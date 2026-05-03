# Design Decisions

**📍 Navigation**
- 🏠 [Documentation Index](../INDEX.md)
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - How components work together
- 💾 [Database Schema](./DATABASE_SCHEMA.md) - Entity relationships
- 🔌 [API Design](./API_DESIGN.md) - REST endpoint patterns
- 📦 [Storage Layer](./STORAGE_LAYER.md) - File storage abstraction
- 🔐 [Authentication](./AUTHENTICATION.md) - OAuth and JWT patterns
- 📚 [All Guides](../Guides/) - Startup, TDD, Docker, CI/CD

---

# PhotoGallery Design Decisions

This document is the **source of truth** for all architectural decisions. Every design decision is recorded here with:
- **Context**: Why was this needed?
- **Decision**: What was decided?
- **Rationale**: Why this approach?
- **Implications**: How does it affect the system?
- **Implementation**: Where is it implemented?
- **Tests**: How do tests validate it?

---

## D001: Microservice Authentication with Google OAuth + Local Database

**Status**: ✅ Approved  
**Date**: Phase 3 (2026-04-15)  
**Approved By**: User  
**Related**: [Authentication.md](./AUTHENTICATION.md)

### Context
Users need social login (Google) plus role-based authorization (Admin/User). System must support future extensibility to Facebook, Microsoft, etc.

### Decision
- **Primary Auth**: Google OAuth 2.0 via external identity provider
- **Local Storage**: EF Core database stores users + roles for authorization
- **API Auth**: JWT Bearer tokens for all authenticated API calls
- **Development**: DISABLE_AUTH middleware bypasses Google for testing
- **Roles**: Two roles (Admin, User) stored locally for fast authorization

### Rationale
- Google handles authentication (secure, user trusts it)
- Local roles enable fast authorization (no external calls needed)
- JWT tokens allow stateless API (scales horizontally)
- DISABLE_AUTH enables fast development without logging in repeatedly
- Role-based access easily extensible (add more roles as needed)

### Implications
- Every authenticated API call includes JWT in Authorization header
- Roles are ALWAYS checked locally (no external auth on every request)
- Development is fast (DISABLE_AUTH=true skips Google)
- Production requires Google OAuth configured
- Future social logins reuse same pattern (add new ExternalAuthProvider)

### Implementation
**Files:**
- `PhotoGallery/Services/ExternalAuthService.cs` - Google OAuth handling
- `PhotoGallery/Services/JwtTokenService.cs` - JWT generation and validation
- `PhotoGallery/Middleware/DisableAuthMiddleware.cs` - Development bypass
- `PhotoGallery/Data/Configurations/UserConfiguration.cs` - Database schema
- `appsettings.json` - Google OAuth configuration

**Tests:**
- `PhotoGallery.Tests/AuthenticationTests.cs` - OAuth flow validation
- `PhotoGallery.Tests/JwtTests.cs` - Token generation and validation
- `PhotoGallery.Tests/AuthorizationTests.cs` - Role-based access control

### Example: Using JWT Token

```csharp
// In any protected API endpoint
[Authorize]
[HttpGet("api/albums")]
public async Task<IActionResult> GetAlbums()
{
    // User is authenticated via JWT token
    // Claims extracted from token include roles
    var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
    
    if (userRole != "Admin")
        return Forbid(); // User doesn't have permission
    
    return Ok(albums);
}
```

### Alternatives Considered
- ❌ Password-based auth: User requirement was social login only
- ❌ External authorization: Would require API call per request (slow)
- ❌ Session-based auth: Doesn't scale horizontally (stateless required)
- ❌ No role caching: Would be very slow (every request hits external service)

---

## D002: Storage Provider Abstraction Layer

**Status**: ✅ Approved  
**Date**: Phase 4 (2026-04-20)  
**Approved By**: User  
**Related**: [Storage Layer](./STORAGE_LAYER.md)

### Context
Photos stored in object storage (MinIO in dev, Azure in prod). Need easy switching between providers without code changes.

### Decision
- **Interface**: `IStorageProvider` with 6 core methods (Upload, Download, Delete, GetUrl, Exists, List)
- **Development**: MinioStorageProvider (MinIO Docker container)
- **Production**: AzureStorageProvider (Azure Blob Storage)
- **Selection**: StorageProviderFactory selects provider via `Environment` setting
- **Configuration**: All settings in `appsettings.json` by environment
- **Path Structure**: All photos stored as `photogallery/{album_guid}/{photo_guid}/{quality}.jpg`

### Rationale
- Interface abstracts storage (easy to add providers: S3, Google Cloud, etc.)
- Development uses MinIO (free, local, no cloud credentials)
- Production uses Azure (fast, reliable, integrated with ecosystem)
- Factory pattern enables runtime selection (no code changes needed)
- Consistent path structure enables data portability
- Configuration-driven (no code changes for deployment)

### Implications
- All storage operations go through `IStorageProvider` interface
- Adding new provider = implement interface + register in factory
- Migration between providers = copy data, change config
- All photo paths follow strict convention (enables versioning, migration, analytics)
- Storage is abstracted from business logic (services don't know if MinIO or Azure)

### Implementation
**Files:**
- `PhotoGallery/Services/Storage/IStorageProvider.cs` - Interface definition
- `PhotoGallery/Services/Storage/MinioStorageProvider.cs` - MinIO implementation
- `PhotoGallery/Services/Storage/AzureStorageProvider.cs` - Azure implementation
- `PhotoGallery/Services/Storage/StorageProviderFactory.cs` - Provider selection
- `PhotoGallery/Controllers/PhotosController.cs` - Uses IStorageProvider
- `appsettings.json` - Storage configuration

**Tests:**
- `PhotoGallery.Tests/StorageProviderTests.cs` - Interface contract validation
- `PhotoGallery.Tests/MinioStorageTests.cs` - MinIO provider testing
- `PhotoGallery.Tests/AzureStorageTests.cs` - Azure provider testing
- `PhotoGallery.Tests/StoragePathTests.cs` - Path structure validation

### Example: Adding New Provider

```csharp
// 1. Implement interface
public class GoogleCloudStorageProvider : IStorageProvider
{
    public async Task<string> UploadAsync(string key, Stream stream, string contentType)
    {
        // Google Cloud implementation
    }
    // ... implement other methods
}

// 2. Register in factory
public class StorageProviderFactory
{
    public static IStorageProvider Create(string provider)
    {
        return provider switch
        {
            "minio" => new MinioStorageProvider(),
            "azure" => new AzureStorageProvider(),
            "googlecloud" => new GoogleCloudStorageProvider(), // NEW!
            _ => throw new InvalidOperationException()
        };
    }
}

// 3. Update appsettings.json
// "Storage": { "Provider": "googlecloud", ... }
```

### Alternatives Considered
- ❌ Hardcoded storage provider: Would require code changes for different environments
- ❌ Mixed storage (MinIO + Azure): Adds complexity, harder to migrate
- ❌ No path structure standard: Would enable inconsistent storage (chaos)

---

## D003: Image Processing with Compression Profiles & Per-Quality Tracking

**Status**: ✅ Approved  
**Date**: Phase 5 (2026-04-25); Enhanced Phase 12 (2026-05-03)  
**Approved By**: User  
**Related**: [System Architecture](./SYSTEM_ARCHITECTURE.md)

### Context
Users need multiple quality versions of photos (high/medium/low/thumbnail) for different use cases. Processing should be asynchronous (user doesn't wait). System must track processing progress per quality, retry failed processing, and maintain consistency between database and storage.

### Decision
- **Profiles**: 4 compression profiles (Thumbnail=200x200, Low=800x800, Medium=1920x1920, High=3840x3840)
- **Queue**: ProcessingQueue entity tracks complete photo processing job
- **Quality Tracking**: ProcessingQueueItem tracks each individual quality (Thumbnail, Low, Medium, High) with separate status
- **Worker**: Background thread polls queue every 5 seconds, processes one item at a time
- **Storage**: All versions stored as separate files: `photogallery/{album}/{photo}/{quality}.jpg`
- **Retry Logic**: Failed items auto-retry up to 3 times with exponential backoff
- **Status**: Queue = Pending → Processing → Complete → Error; Each Quality Item = Pending → Processing → Complete → Error
- **Consistency**: Consistency checker runs hourly to verify all 4 qualities exist for each complete photo

### Rationale
- Per-quality tracking shows which versions are complete (enables partial processing visibility)
- Separate ProcessingQueueItem entities allow independent retry and status tracking
- Multiple profiles support different download use cases (email, web, print, thumbnail previews)
- Asynchronous processing improves user experience (upload returns immediately)
- Retry logic with backoff prevents transient failures from losing photos
- Consistency checker prevents storage/database mismatch (critical data integrity)
- Background worker uses dedicated thread (doesn't block API)
- Compression profiles centralized (easy to adjust percentages without code changes)

### Implications
- Upload returns immediately with job ID
- Client can poll for per-quality progress (`GET /api/photos/{id}/processing-status`)
- All versions of same photo stored separately and tracked individually
- Failed processing automatically retries (improves reliability)
- Consistency checker alerts if photos missing versions
- Storage and database always in sync (no orphaned files or missing data)
- Image processing is abstracted (can use different libraries)

### Implementation
**Database Schema:**
```sql
ProcessingQueue
  - Id: Guid (PK)
  - PhotoId: Guid (FK)
  - CreatedAt: DateTime
  - CompletedAt: DateTime (nullable)
  - Status: enum (Pending, Processing, Complete, Error)
  - ErrorMessage: string (nullable)

ProcessingQueueItem
  - Id: Guid (PK)
  - ProcessingQueueId: Guid (FK)
  - Quality: enum (Thumbnail, Low, Medium, High)
  - Status: enum (Pending, Processing, Complete, Error)
  - RetryCount: int (0-3)
  - LastError: string (nullable)
  - Attempts: int
  - NextRetryTime: DateTime (nullable)
```

**Files:**
- `PhotoGallery/Models/ProcessingQueue.cs` - Main queue entity
- `PhotoGallery/Models/ProcessingQueueItem.cs` - Per-quality tracking (NEW)
- `PhotoGallery/Services/Processing/ImageProcessingService.cs` - Queue worker
- `PhotoGallery/Services/Processing/CompressionProfile.cs` - Profile definitions
- `PhotoGallery/Services/Processing/PhotoConsistencyChecker.cs` - Validates storage integrity (NEW)
- `PhotoGallery/Controllers/PhotosController.cs` - Upload & status endpoints
- `PhotoGallery/Data/Configurations/ProcessingQueueConfiguration.cs` - EF configuration
- `PhotoGallery/Data/Configurations/ProcessingQueueItemConfiguration.cs` - EF configuration (NEW)

**Tests:**
- `PhotoGallery.Tests/ImageProcessingTests.cs` - Processing logic
- `PhotoGallery.Tests/ProcessingQueueTests.cs` - Queue entity and status transitions
- `PhotoGallery.Tests/ProcessingQueueItemTests.cs` - Per-quality tracking (NEW)
- `PhotoGallery.Tests/CompressionProfileTests.cs` - Profile validation
- `PhotoGallery.Tests/RetryLogicTests.cs` - Retry with backoff behavior (NEW)
- `PhotoGallery.Tests/PhotoConsistencyCheckerTests.cs` - Storage integrity validation (NEW)

### Example: Queuing & Tracking Photo Processing

```csharp
// Upload endpoint queues photo with per-quality tracking
[HttpPost("api/photos/albums/{albumId}")]
public async Task<IActionResult> UploadPhotos(Guid albumId, IFormFile file)
{
    var photo = Photo.Create(albumId, file);
    await photoRepository.AddAsync(photo);
    
    // Queue for processing (returns job ID immediately)
    var jobId = await imageProcessor.QueuePhotoAsync(photo.Id);
    // Creates: ProcessingQueue with status=Pending
    // Creates: 4 x ProcessingQueueItem (Thumbnail, Low, Medium, High, each status=Pending)
    
    return Ok(new { jobId, status = "queued" });
}

// Client polls for detailed per-quality progress
[HttpGet("api/photos/{photoId}/processing-status")]
public async Task<IActionResult> GetProcessingStatus(Guid photoId)
{
    var queue = await processingQueue.GetByPhotoIdAsync(photoId);
    
    return Ok(new
    {
        jobId = queue.Id,
        overallStatus = queue.Status, // Pending, Processing, Complete, Error
        qualities = new
        {
            thumbnail = new { status = "complete", retries = 0 },
            low = new { status = "complete", retries = 0 },
            medium = new { status = "complete", retries = 0 },
            high = new { status = "processing", retries = 1, estimatedSeconds = 15 }
        },
        progress = "75%", // 3 of 4 complete
        createdAt = queue.CreatedAt,
        completedAt = queue.CompletedAt
    });
}

// Background worker processes queue items with retry logic
public class ImageProcessingWorker
{
    public async Task ProcessQueueAsync()
    {
        while (true)
        {
            var pendingItems = await processingQueue.GetPendingItemsAsync();
            
            foreach (var item in pendingItems)
            {
                try
                {
                    // Process single quality
                    await ProcessQualityAsync(item);
                    item.MarkComplete();
                }
                catch (Exception ex)
                {
                    // Auto-retry with backoff
                    if (item.RetryCount < 3)
                    {
                        item.IncrementRetry();
                        item.NextRetryTime = DateTime.UtcNow.AddSeconds(
                            Math.Pow(2, item.RetryCount)); // Exponential backoff
                    }
                    else
                    {
                        item.MarkError(ex.Message);
                    }
                }
            }
            
            await Task.Delay(5000); // Poll every 5 seconds
        }
    }
}

// Consistency checker ensures no orphaned photos
public class PhotoConsistencyChecker
{
    public async Task CheckConsistencyAsync()
    {
        var incompletePhotos = await processingQueue.GetCompletePhotosAsync();
        
        foreach (var photo in incompletePhotos)
        {
            var qualities = new[] { "thumbnail", "low", "medium", "high" };
            var missingQualities = new List<string>();
            
            foreach (var quality in qualities)
            {
                var path = $"photogallery/{photo.AlbumId}/{photo.Id}/{quality}.jpg";
                if (!await storage.ExistsAsync(path))
                    missingQualities.Add(quality);
            }
            
            if (missingQualities.Count > 0)
                logger.Error($"Photo {photo.Id} missing: {string.Join(", ", missingQualities)}");
        }
    }
}
```

### Alternatives Considered
- ❌ Synchronous processing: Would block user (bad UX)
- ❌ Single quality: Doesn't meet user requirements
- ❌ External processing service: Adds complexity, cost

---

## D004: TDD as Development Standard

**Status**: ✅ Approved  
**Date**: Phase 12 (2026-05-02)  
**Approved By**: User  
**Related**: [TDD Workflow](../Guides/TDD_WORKFLOW.md)

### Context
Previous development was ad-hoc, tested after implementation (or not at all). Need consistent quality, regression prevention, and safe refactoring.

### Decision
- **Tests First**: All features start with xUnit tests in PhotoGallery.Tests
- **RED-GREEN-BLUE**: Mandatory workflow (RED: write failing tests, GREEN: implement, BLUE: refactor)
- **Coverage**: 100% of features have tests
- **Passing**: ALL tests must pass before committing
- **Isolation**: Tests use Moq for external dependencies (storage, databases, APIs)

### Rationale
- Tests are specification (define requirements before coding)
- TDD prevents regressions (tests catch breaking changes immediately)
- Tests enable safe refactoring (change code with confidence)
- Tests serve as documentation (show how to use code)
- xUnit + Moq are standard for .NET (familiar, well-documented)

### Implications
- Feature development takes longer initially (but catches bugs early)
- Code quality improves (loose coupling required for testability)
- Refactoring is safe (tests verify nothing breaks)
- New developers onboard faster (tests show how code works)
- No legacy code (all code has tests from day one)

### Implementation
**Files:**
- `PhotoGallery.Tests/` - Test project
- `skills/tdd-unit-testing-skill/SKILL.md` - TDD patterns and guidance
- `Documentation/Guides/TDD_WORKFLOW.md` - Step-by-step TDD process
- `Documentation/Guides/TDD_QUICK_REFERENCE.md` - Developer cheat sheet

**Workflow:**
1. Design test cases (consult TDD skill)
2. Write failing tests (RED)
3. Implement code (GREEN)
4. Refactor while tests pass (BLUE)
5. Consult architect (SOLID/DRY review)
6. Commit tests + implementation together

### Example: TDD for Photo Upload

```csharp
// Step 1: Write FAILING test
[Fact]
public void UploadPhoto_Should_Queue_For_Processing()
{
    // Arrange
    var albumId = Guid.NewGuid();
    var file = CreateTestFile("test.jpg");
    
    // Act
    var result = await photoController.UploadPhoto(albumId, file);
    
    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value as dynamic;
    Assert.NotNull(response.jobId);
}
// Test FAILS (class doesn't exist yet)

// Step 2: Write MINIMAL implementation
public class PhotosController
{
    [HttpPost("api/photos/albums/{albumId}")]
    public async Task<IActionResult> UploadPhoto(Guid albumId, IFormFile file)
    {
        var jobId = Guid.NewGuid();
        return Ok(new { jobId });
    }
}
// Test PASSES

// Step 3: Refactor while keeping test GREEN
public class PhotosController
{
    private readonly IPhotoService photoService;
    
    public PhotosController(IPhotoService photoService)
    {
        this.photoService = photoService;
    }
    
    [HttpPost("api/photos/albums/{albumId}")]
    public async Task<IActionResult> UploadPhoto(Guid albumId, IFormFile file)
    {
        var photo = Photo.Create(albumId, file);
        var jobId = await photoService.QueueForProcessing(photo);
        return Ok(new { jobId });
    }
}
// Test still PASSES
```

### Alternatives Considered
- ❌ Test after implementation: Defeats purpose (bugs already in production)
- ❌ No tests: Leads to technical debt, regressions, fear of changes
- ❌ Only E2E tests: Too slow, hard to debug, miss edge cases

---

## D005: Storage Path Structure Standard

**Status**: ✅ Approved  
**Date**: Phase 12 (2026-05-02)  
**Approved By**: User  
**Related**: [Storage Layer](./STORAGE_LAYER.md)

### Context
Multiple photo versions (original, high, medium, low, raw) need consistent organization in object storage. Need structure that enables analytics, migration, and data integrity.

### Decision
```
photogallery/
├── {album_guid}/           # Album namespace
│   └── {photo_guid}/       # Photo namespace
│       ├── original.jpg    # Uploaded file (no processing)
│       ├── high.jpg        # 50% compression
│       ├── medium.jpg      # 75% compression
│       ├── low.jpg         # 85% compression
│       └── raw.jpg         # 100% quality
```

### Rationale
- **Hierarchical**: Enables easy querying (list all photos in album)
- **Versioning**: Each quality stored as separate file (no naming conflicts)
- **Migration**: Easy to copy albums between storage backends
- **Analytics**: Can aggregate stats by album or photo
- **Extensibility**: Easy to add new qualities (thumbnail, preview, etc.)

### Implications
- All storage code must use this path structure
- Tests validate path compliance
- Migration between storage providers preserves structure
- Reporting can traverse this structure
- Backup/restore tools understand this structure

### Implementation
**Files:**
- `PhotoGallery/Controllers/PhotosController.cs` - Line 100: constructs path
- `PhotoGallery/Services/Processing/ImageProcessingService.cs` - Line 223: constructs path
- `PhotoGallery.Tests/PhotoUploadTests.cs` - Validates path structure

**Tests:**
- `PhotoGallery.Tests/PhotoUploadTests.cs` - Path format validation
- `PhotoGallery.Tests/StoragePathTests.cs` - Path consistency across features

### Example: Verifying Path Structure

```csharp
[Fact]
public void Photo_StoragePath_Should_Follow_Convention()
{
    // Arrange
    var albumId = Guid.Parse("c373c777-c47a-4648-937e-c92006319c30");
    var photoId = Guid.NewGuid();
    
    // Act
    var storagePath = $"photogallery/{albumId}/{photoId}/original.jpg";
    
    // Assert
    Assert.StartsWith("photogallery/", storagePath);
    Assert.Contains(albumId.ToString(), storagePath);
    Assert.Contains(photoId.ToString(), storagePath);
    Assert.EndsWith("original.jpg", storagePath);
}
```

### Alternatives Considered
- ❌ Flat structure: Hard to query, organize, or migrate
- ❌ No version suffix: Naming conflicts, can't store multiple qualities
- ❌ UUID in filename: Unreadable, hard to debug

---

## How to Add New Design Decisions

When a new feature is proposed:

1. **Consult this document** - Check if decision already exists
2. **Ask architect** - For design guidance (consult `photogallery-architect-skill`)
3. **Get user approval** - User must approve design
4. **Document here** - Add decision with all sections filled out
5. **Link everywhere** - Update related documents with links to new decision
6. **Implement with TDD** - Write tests, then code, following this design
7. **Validate with tests** - Tests prove design is implemented correctly

---

## Related Documentation

- 🏠 [Documentation Index](../INDEX.md) - Start here for navigation
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Visual component overview
- 💾 [Database Schema](./DATABASE_SCHEMA.md) - Entity relationships and constraints
- 🔌 [API Design](./API_DESIGN.md) - REST endpoint patterns and conventions
- 📦 [Storage Layer](./STORAGE_LAYER.md) - File storage implementation details
- 🔐 [Authentication](./AUTHENTICATION.md) - OAuth 2.0 and JWT implementation

---

**Last Updated**: 2026-05-03  
**Total Decisions**: 5  
**All Approved**: ✅ Yes  
**In Implementation**: All phases through Phase 12
