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

## D006: Frontend Testing Strategy — Playwright-First for User-Visible Behavior

**Status**: ✅ Approved  
**Date**: Phase 13 (2026-05-04)  
**Approved By**: User  
**Related**: [D004 TDD Standard](#d004-tdd-as-development-standard) • [Pre-Implementation Checklist](../Guides/PRE-IMPLEMENTATION-CHECKLIST.md)

### Context
The Angular frontend ships with Karma + Jasmine, but existing specs only cover trivial component constructors. A real-world bug surfaced (broken-thumbnail icon on album-detail cards while the upload component shows the correct image) that lives in the *integration* between three layers:
1. Backend pre-signed URL generation (`PhotoVersionUrlService`).
2. MinIO storage state (object actually present or not).
3. Browser image loading (`<img>` element behavior on 404).

Karma specs with mocked `HttpClient` cannot observe any of these interactions. The bug is invisible to unit tests by construction. We need a frontend test strategy that exercises the real browser, real backend, and real storage in concert.

### Decision
- **Primary frontend test tier**: **Playwright** end-to-end tests under `FE.PhotoGallery/e2e/` against a running backend (`http://localhost:5026`) and live MinIO container.
- **Lean Page Object Model**: tests reference `e2e/pages/*.page.ts` *only when a page object actually pays for itself* — that is, when more than one spec uses the same selectors or the same flow. For v1 (two specs), we still introduce page objects for the two screens we touch (`AlbumDetailPage`, `PhotoUploadPage`) because those screens are reused across both specs and are the natural future extension points. We do NOT introduce a `BasePage` abstract class until a third page proves the abstraction is needed (YAGNI).
- **Fixtures** (`e2e/fixtures/*.fixture.ts`): authenticated browser context, sample photo paths from `SamplePhotos/`.
- **Helpers** (`e2e/helpers/*.ts`): polling helpers (`wait-for-processing.ts`), assertion helpers (`assert-image-loads.ts`).
- **Multi-browser**: Chromium, Firefox, WebKit (per existing `playwright.config.ts`).
- **Karma is retained for pure component logic** (formatters, pipes, simple input validation). It receives no new investment for integration scenarios.
- **Stable selectors**: components expose `data-testid` attributes; tests never select on CSS classes or text content.

### Rationale
- The most important class of frontend bugs in PhotoGallery are integration bugs (URL generation × storage state × DOM behavior). Playwright is the only tier that catches them.
- Page Object Model produces tests that survive UI refactoring — selectors live in one place per page.
- `data-testid` attributes are immune to styling and i18n changes.
- Real browser execution catches issues mocks would miss: image decoding, CORS, cookie behavior, redirect handling, pre-signed URL signature validation.
- Avoids the well-known anti-pattern of "100% green Karma + production bug": mocked HTTP responses lie because they only reflect the developer's mental model.

### Implications
- Every new user-visible feature ships with a Playwright spec under `e2e/`.
- New components get `data-testid` attributes from the start.
- VS Code tasks `Tests: Frontend E2E (Playwright)` and `Tests: Frontend E2E UI Mode` provide one-click runs.
- CI must run `npm run e2e` against a real backend + MinIO (Docker Compose).
- `qa-quality-control-skill` owns E2E test development; `playwright-testing-skill` provides the Page Object Model template.

### Implementation
**Files (new):**
- `FE.PhotoGallery/e2e/pages/album-detail.page.ts` — album cards, photo grid.
- `FE.PhotoGallery/e2e/pages/photo-upload.page.ts` — file chooser, in-component thumbnails.
- `FE.PhotoGallery/e2e/fixtures/auth.fixture.ts` — authenticated context.
- `FE.PhotoGallery/e2e/fixtures/data.fixture.ts` — sample photos.
- `FE.PhotoGallery/e2e/helpers/wait-for-processing.ts` — polls `GET /api/photos/{photoId}/status` until `percentComplete === 100`.
- `FE.PhotoGallery/e2e/helpers/assert-image-loads.ts` — verifies `naturalWidth > 0` and `complete === true`.
- `FE.PhotoGallery/e2e/photo-upload-and-display.spec.ts` — regression spec for the broken-thumbnail bug.
- `FE.PhotoGallery/e2e/admin-reconcile.spec.ts` — exercises the D007 admin endpoint.

> Intentionally NOT created in v1: `e2e/pages/base.page.ts`, `e2e/pages/login.page.ts`. We use the existing `DISABLE_AUTH=true` Dev mode to skip login entirely, and we don't need a base class for two page objects. Add when justified.

**Files (modified):**
- `FE.PhotoGallery/src/app/components/albums/album-detail.component.ts` — add `data-testid` attributes.
- `FE.PhotoGallery/src/app/components/albums/photo-upload.component.ts` — add `data-testid` attributes.
- `.vscode/tasks.json` — add Playwright tasks.

**Tests:**
- All Playwright specs run via `npm run e2e` from `FE.PhotoGallery/`.
- Existing Karma specs (`*.spec.ts` in `src/`) remain green; they are not migrated.

### Example: Page Object + Spec
```typescript
// e2e/pages/album-detail.page.ts
import { Page, Locator, expect } from '@playwright/test';

export class AlbumDetailPage {
  readonly photoCards: Locator;

  constructor(readonly page: Page) {
    this.photoCards = page.getByTestId('photo-card');
  }

  async goto(albumId: string) {
    await this.page.goto(`/albums/${albumId}`);
    await expect(this.page.getByTestId('album-detail-root')).toBeVisible();
  }

  async assertAllImagesLoaded() {
    const cards = await this.photoCards.all();
    for (const card of cards) {
      const img = card.getByTestId('photo-card-image');
      await expect(img).toBeVisible();
      const naturalWidth = await img.evaluate((el: HTMLImageElement) => el.naturalWidth);
      expect(naturalWidth).toBeGreaterThan(0);
    }
  }
}

// e2e/photo-upload-and-display.spec.ts
import { test } from './fixtures/auth.fixture';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { waitForProcessing } from './helpers/wait-for-processing';
import { createAlbumViaApi } from './helpers/api';

test('uploaded photo appears with valid thumbnail in album cards', async ({ page, sampleJpeg }) => {
  const album = await createAlbumViaApi(page);
  const detail = new AlbumDetailPage(page);
  await detail.goto(album.id);

  // The upload response carries the photoId we'll poll on, NOT the albumId.
  const { photoId } = await new PhotoUploadPage(page).uploadFile(sampleJpeg);
  await waitForProcessing(page, photoId);

  await page.reload();
  await detail.assertAllImagesLoaded();
});
```

### Alternatives Considered
- ❌ **Karma for everything**: Cannot observe real browser image-load behavior. Misses the entire bug class that prompted this decision.
- ❌ **Cypress**: Single-process, single-tab, weaker multi-browser story. Playwright is already configured in this repo.
- ❌ **Manual QA only**: Doesn't scale, regresses constantly, defeats CI value.
- ❌ **Migrate existing Karma specs**: Sunk cost; existing specs work for what they test, just don't expand their scope.

---

## D007: Storage/Database Consistency Reconciliation

**Status**: ✅ Approved  
**Date**: Phase 13 (2026-05-04)  
**Approved By**: User  
**Related**: [D003 Image Processing](#d003-image-processing-with-compression-profiles--per-quality-tracking) • [D005 Storage Path Standard](#d005-storage-path-structure-standard)

### Context
The existing `PhotoConsistencyChecker` only validates `ProcessingQueueItem` *records*: it answers "does this photo have 4 queue items, all Complete, covering all 4 qualities?" — but it **never** verifies actual storage objects exist. Drift accumulates over time:
- A photo's `ProcessingQueueItem` rows say `Complete`, but `thumbnail.jpg` is missing from MinIO (manual deletion, failed retention sweep, bucket migration).
- A storage object exists but no `ProcessingQueueItem` row tracks it (recovered from backup without DB sync).
- `original.jpg` is gone but the `Photo` row remains.

Symptom: the user reports cards showing broken-image icons because the `ThumbnailUrl` in the album-photos response points to a deleted MinIO object. The cached pre-signed URL is "valid" by signature; the storage object is not.

### Decision
A new **`StorageConsistencyService`** (scoped) reconciles DB state against storage state. It is invoked from two places:
1. A new **`StorageConsistencyWorker`** `BackgroundService` runs hourly (configurable).
2. A new admin endpoint `POST /api/photos/admin/reconcile-storage` (`[Authorize(Roles="Admin")]`) **synchronously** triggers it on demand and returns the summary report. This is intentional for v1 — admins are technical users with no SLA, the dataset bounded by photo count is small in practice, and a sync endpoint sidesteps the complexity of a separate job-status table. (Future v2 may move to 202 Accepted + job-id polling if the sync wait becomes painful.)

The service classifies each `(photoId, quality)` pair into one of four cases:

| Storage state | Queue item state | Action |
|---------------|------------------|--------|
| Missing | None | Insert `Pending` `ProcessingQueueItem` (regenerate from `original.jpg`). Ensure parent `ProcessingQueue` exists; if not, create it as `Pending`. |
| Missing | `Complete` | Flip item to `Pending`, reset `RetryCount=0`, `LastError=null`, `NextRetryTime=null`, `CompletedAt=null`. Then **invalidate the matching active `PhotoVersionUrl` row** (set `IsActive=false`) so the album-list endpoint stops returning the now-broken URL; D008's cache-write path will overwrite-in-place via `GetByPhotoAndQualityIncludingInactiveAsync` the next time the URL is regenerated, so the unique `(PhotoId, Quality)` index is never violated. Reopen the parent `ProcessingQueue` if it was `Complete` (set `Status=Pending`, `CompletedAt=null`, `ErrorMessage=null`). |
| Present | None | Insert `Complete` `ProcessingQueueItem` with `CompletedAt = UtcNow` (back-fill DB record). Ensure parent `ProcessingQueue` exists; if not, create it. Then call the existing `PhotoConsistencyChecker.MarkQueueCompleteIfReadyAsync` to re-derive queue status. |
| Present | `Complete` | No-op. |

Edge cases (explicit rules):
- **`Photo` row exists but no `ProcessingQueue`**: an upload completed `_photoRepository.AddAsync` but `_imageProcessor.QueuePhotoAsync` failed (`PhotosController.cs:154-158` swallows that exception by design). The reconciler **creates** the missing `ProcessingQueue`, then proceeds with the four-quality classification above.
- **`original.jpg` missing**: log a warning at WARN level. Do NOT auto-delete the `Photo` row in v1 (too destructive). Skip per-quality reconciliation for that photo since regeneration would fail anyway.
- **Items in `Processing` state**: skip — never touch in-flight items. This is the closest the worker comes to coordinating with `PhotoProcessingWorker`.
- **Items in `Error` state with `RetryCount >= MaxRetries`**: leave alone (don't re-queue exhausted-retry items forever). Log at INFO level so admins can intervene.
- **Items in `Error` state with `RetryCount < MaxRetries`**: treat as Pending if storage is missing (reset retry metadata as above); treat as Complete if storage is present.

The service exposes `Task<ConsistencyReport> RunOnceAsync(CancellationToken)` returning per-photo and aggregate counts.

> **Concurrency note (best-effort, not strict-mutual-exclusion)**: `ProcessingQueueItemRepository.GetPendingItemsAsync` is a plain `SELECT` with no atomic claim/update (`ProcessingQueueItemRepository.cs:17-23`). The "skip Processing items" rule reduces the race window but does not eliminate it: between the consistency service reading an item as `Complete` and writing it back as `Pending`, `PhotoProcessingWorker` could theoretically claim and start processing the same item. Under the current single-instance, single-worker assumption this is acceptable — the worst outcome is a duplicate processing run on a single quality, which is idempotent (overwrites the same storage key). We document this explicitly so a future move to multi-instance hosting triggers a coordination redesign (e.g., row-version tokens, advisory locks, or a queue claim table).

> **Worker overlap (admin trigger vs hourly tick)**: a `SemaphoreSlim(1, 1)` guards `RunOnceAsync` so two concurrent invocations (admin endpoint + worker tick) serialize. The second caller awaits the first.

### Rationale
- **Separation of concerns**: `PhotoConsistencyChecker` validates queue records (D003 concern); `StorageConsistencyService` validates storage objects (D005 concern). They answer different questions.
- **Storage-as-presence-source-of-truth**: when DB and storage disagree on *whether a quality version exists*, real bytes on disk are the ground truth — back-fill or re-queue accordingly. Note: D007 repairs **presence drift only**, not file integrity. A corrupted-but-present file is still classified `Complete`. File integrity validation (e.g., re-decoding to verify the file is a valid JPEG) is explicitly out of scope for v1; if a corrupt file ships from broken hardware or partial uploads, that is a separate problem with separate solutions (storage-layer checksums, re-validation worker).
- **Idempotent**: running the service N times produces the same result as running it once.
- **Best-effort concurrency**: under single-worker assumptions, "skip Processing items" is sufficient. The semaphore prevents admin/worker double-runs. A future move to multi-instance hosting would need a real coordination mechanism — flagged in the implementation comments.
- **On-demand admin trigger**: lets admins re-converge after manual storage operations without waiting for the next worker tick.

### Implications
- Two new production files: `Services/Processing/StorageConsistencyService.cs` and `Services/Processing/StorageConsistencyWorker.cs`.
- One new admin endpoint: `POST /api/photos/admin/reconcile-storage`.
- Three new config keys: `PhotoProcessing:ConsistencyCheckIntervalHours` (default 1), `PhotoProcessing:ConsistencyCheckEnabled` (default true), and (per D008) `BlobStorage:VerifyCachedUrls`.
- One new xUnit test file: `PhotoGallery.Tests/StorageConsistencyServiceTests.cs`.
- One new Playwright spec: `e2e/admin-reconcile.spec.ts`.
- The existing `PhotoProcessingWorker` automatically picks up `Pending` items the consistency service inserts — no changes needed there.

### Implementation
**Files (new):**
- `PhotoGallery/Services/Processing/StorageConsistencyService.cs` — scoped, contains the `RunOnceAsync` logic.
- `PhotoGallery/Services/Processing/StorageConsistencyWorker.cs` — `BackgroundService` mirroring `PhotoProcessingWorker`'s `PeriodicTimer` pattern.
- `PhotoGallery.Tests/StorageConsistencyServiceTests.cs` — RED tests covering all four classification cases.

**Files (modified):**
- `PhotoGallery/Controllers/PhotosController.cs` — add admin endpoint.
- `PhotoGallery/Program.cs` — register `AddScoped<StorageConsistencyService>()` and `AddHostedService<StorageConsistencyWorker>()`.
- `PhotoGallery/appsettings.json` — add the three config keys.

**Tests:**
- xUnit tests use `Moq` for `IStorageProvider`, `IPhotoRepository`, `IProcessingQueueItemRepository`, `IProcessingQueueRepository`, `IPhotoVersionUrlRepository`.
- Tests verify each of the four classification cases plus: original-missing, missing-`ProcessingQueue` for a `Photo`, items in `Processing` state are skipped, `Error` items at max retries are left alone, retry metadata reset on `Error→Pending`, idempotency, and the semaphore prevents concurrent `RunOnceAsync` invocations.
- Playwright spec uploads a photo, **captures the photoId from the upload response** (`PhotoUploadInfo.PhotoId` per `PhotosController.cs:147-152`), waits for processing using `GET /api/photos/{photoId}/status` until `percentComplete = 100`, then deletes one quality from MinIO via the S3 client, calls the admin reconcile endpoint synchronously (returns when reconciliation completes), and asserts all four qualities are present in storage and the photo card image loads.

### Example: Service Shape
```csharp
public class StorageConsistencyService
{
    // The four storage-relevant qualities. Note: D005 documents storage paths as
    // {original/high/medium/low/raw}.jpg, but the actual QualityType enum is
    // Thumbnail/Low/Medium/High (no raw). This is a known pre-existing doc/code
    // drift in D005 — D007 follows the enum, since that's what ImageProcessingService
    // and PhotoVersionUrlService both use today. A separate cleanup pass should
    // either update D005 or add a 'raw' quality to align them; that work is out
    // of scope for D007.
    private static readonly QualityType[] AllQualities =
    {
        QualityType.Thumbnail,
        QualityType.Low,
        QualityType.Medium,
        QualityType.High,
    };

    private readonly SemaphoreSlim _runLock = new(1, 1);

    public async Task<ConsistencyReport> RunOnceAsync(CancellationToken ct)
    {
        await _runLock.WaitAsync(ct);
        try
        {
            var report = new ConsistencyReport();

            await foreach (var photo in _photoRepo.StreamAllAsync(ct))
            {
                var prefix = $"photogallery/{photo.AlbumId}/{photo.Id}/";
                var presentKeys = (await _storage.ListAsync(prefix)).ToHashSet();

                if (!presentKeys.Contains($"{prefix}original.jpg"))
                {
                    _logger.LogWarning("Photo {PhotoId} missing original.jpg in storage", photo.Id);
                    report.MissingOriginalCount++;
                    continue; // No source to regenerate from.
                }

                var queue = await EnsureQueueAsync(photo.Id, ct);
                var items = (await _itemRepo.GetByPhotoIdAsync(photo.Id)).ToList();

                foreach (var quality in AllQualities)
                {
                    var key = $"{prefix}{quality.ToString().ToLower()}.jpg";
                    var present = presentKeys.Contains(key);
                    var item = items.FirstOrDefault(i => i.Quality == quality);
                    await ReconcileAsync(photo, queue, quality, present, item, report, ct);
                }
            }

            _logger.LogInformation("Consistency cycle: {Report}", report);
            return report;
        }
        finally
        {
            _runLock.Release();
        }
    }
}
```

### Alternatives Considered
- ❌ **Extend `PhotoConsistencyChecker`**: Conflates two concerns (queue-record validation vs storage-object validation). Violates SRP.
- ❌ **Auto-delete orphan storage objects** (object exists but no `Photo` row): too destructive for v1; can be added later behind a flag.
- ❌ **Auto-delete `Photo` rows with missing `original.jpg`**: same reasoning — log a warning instead.
- ❌ **No worker, only on-demand admin endpoint**: drift accumulates silently between admin actions; unacceptable.
- ❌ **Run on every photo upload**: massive overhead (ListAsync per upload); the worker model amortizes the cost.

---

## D008: Cached Pre-Signed URL Storage Verification

**Status**: ✅ Approved  
**Date**: Phase 13 (2026-05-04)  
**Approved By**: User  
**Related**: [D002 Storage Provider Abstraction](#d002-storage-provider-abstraction-layer) • [D007 Storage/Database Consistency Reconciliation](#d007-storagedatabase-consistency-reconciliation)

### Context
`PhotoVersionUrlService.GetPhotoVersionUrlAsync` returns cached pre-signed URLs from the `PhotoVersionUrl` table when they are `IsActive=true` and not yet expired. It performs **no check** that the underlying storage object still exists. When the object has been deleted (manual cleanup, failed sweep, drift between DB and storage), the cached URL is "valid" by signature — MinIO verifies the signature and then returns 404. The browser renders the broken-image icon.

The internal `GeneratePhotoVersionUrlAsync` method already does an `ExistsAsync` check before generating a URL. Only the cached-return path on lines 60–72 of `PhotoVersionUrlService.cs` lacks it.

### Decision
When returning a cached pre-signed URL:
1. Call `IStorageProvider.ExistsAsync(storageKey)` first (controlled by the `BlobStorage:VerifyCachedUrls` config key).
2. If the object exists, return the cached URL as today.
3. If the object is missing, **regenerate by overwriting the existing row in place** (do NOT insert a new row), then fall through to `GeneratePhotoVersionUrlAsync` (which itself returns `null` if the object is genuinely gone — letting the caller surface a placeholder).

> **Why "overwrite in place" instead of "set IsActive=false then insert new"**: the table has a unique index on `(PhotoId, Quality)` regardless of `IsActive` (`PhotoVersionUrlConfiguration.cs:35`). Inserting a second row would throw `UNIQUE constraint failed`. The repository's existing `GetByPhotoAndQualityAsync` filters by `IsActive` (returns null for inactive rows), so the cache-write path would currently fail to find the inactive row and try to insert a duplicate. To enable the in-place overwrite, **`IPhotoVersionUrlRepository` gains a new method `GetByPhotoAndQualityIncludingInactiveAsync`**, and `PhotoVersionUrlService.CachePhotoVersionUrlAsync` uses it for the upsert lookup. Existing read-path callers continue to use the active-only variant.

> **Race window (eventual invalidation, not strict prevention)**: between thread A's `ExistsAsync(false)` and A's row update, thread B may read the same active row and return its stale URL. We accept this — at most one stale response leaks per drift event, and the next call self-heals. Documented as eventual invalidation, not strict prevention.

Config gate `BlobStorage:VerifyCachedUrls` defaults:
- **Development**: `true` — catches drift immediately during testing.
- **Production**: `true` — verification cost (1 HEAD per cached URL on cold-cache lookups) is small relative to the cost of a user seeing broken images. Operators may flip to `false` once D007's worker has been keeping prod drift at zero for a sustained period.

### Rationale
- **Correctness over latency**: an extra HEAD request per cached URL is far cheaper than rendering a broken image to the user.
- **Self-healing in place**: overwriting the existing row triggers regeneration on the same call, which writes a fresh URL pointing to the (presumably re-created) object. Subsequent callers see the fresh URL.
- **Avoids unique-constraint violation**: the `(PhotoId, Quality)` unique index prevents inserting a second row, so the design must update the existing row rather than insert a sibling.
- **Minimal code change**: one new method call (`ExistsAsync`), one new repository method (`GetByPhotoAndQualityIncludingInactiveAsync`), and a small change to `CachePhotoVersionUrlAsync` to use it. No API surface changes.

### Implications
- Album list endpoints have +1 storage HEAD per cached URL when the cache is cold or when `VerifyCachedUrls=true`. Acceptable.
- `IPhotoVersionUrlRepository` adds `GetByPhotoAndQualityIncludingInactiveAsync` (used only by the cache-write path).
- `PhotoVersionUrlServiceTests.cs` gains tests covering: (a) cached-URL-points-to-missing-file regenerates and overwrites the row in place; (b) overwrite reactivates a previously-inactive row without violating the unique constraint; (c) eventual-invalidation race is documented and accepted.
- Frontend gains a defensive `(error)` handler on `<img>` tags so the existing SVG placeholder shows even if a stale URL slips through during the race window.

### Implementation
**Files (modified):**
- `PhotoGallery/Services/PhotoVersionUrlService.cs` — modify cached-return path to verify with `ExistsAsync`; on miss, regenerate (which now overwrites the existing row in place via the new repository method).
- `PhotoGallery/Interfaces/IPhotoVersionUrlRepository.cs` — add `GetByPhotoAndQualityIncludingInactiveAsync`.
- `PhotoGallery/Data/Repositories/PhotoVersionUrlRepository.cs` — implement the new method (same query, no `IsActive` filter).
- `PhotoGallery/appsettings.json` — add `BlobStorage:VerifyCachedUrls = true`.
- `PhotoGallery/appsettings.Production.json` — set `BlobStorage:VerifyCachedUrls = true` (operators may flip to false later).
- `FE.PhotoGallery/src/app/components/albums/album-detail.component.ts` — add `(error)` handler that clears `photo.thumbnailUrl` so the SVG placeholder shows.
- `FE.PhotoGallery/src/app/components/albums/photo-upload.component.ts` — add the same defensive `(error)` handler.

**Tests:**
- `PhotoGallery.Tests/PhotoVersionUrlServiceTests.cs` — new tests: cached-URL-points-to-missing-file triggers regeneration; regeneration overwrites the existing row (no unique-constraint violation); inactive row is reactivated by overwrite.
- `FE.PhotoGallery/e2e/photo-upload-and-display.spec.ts` — verifies images load (`naturalWidth > 0`) for every photo card.

### Example: Modified Cached Path
```csharp
if (cachedUrl != null && cachedUrl.IsActive && cachedUrl.ExpiresAt > DateTime.UtcNow)
{
    if (_verifyCachedUrls)
    {
        var storageKey = BuildStorageKey(photo, quality);
        var stillExists = await _storageProvider.ExistsAsync(storageKey);
        if (!stillExists)
        {
            _logger.LogWarning("Cached URL for {PhotoId}/{Quality} points to missing object; regenerating",
                photoId, quality);
            // GeneratePhotoVersionUrlAsync calls CachePhotoVersionUrlAsync, which now uses
            // GetByPhotoAndQualityIncludingInactiveAsync to find the existing row and overwrite it.
            // This avoids the unique-constraint violation that would occur if we inserted a new row.
            return await GeneratePhotoVersionUrlAsync(photoId, quality, shouldCache);
        }
    }
    return cachedUrl.PresignedUrl;
}
```

```csharp
// PhotoVersionUrlRepository.cs (new method)
public async Task<PhotoVersionUrl?> GetByPhotoAndQualityIncludingInactiveAsync(Guid photoId, QualityType quality)
{
    return await _dbSet
        .Where(pvu => pvu.PhotoId == photoId && pvu.Quality == quality)
        .FirstOrDefaultAsync();
}
```

### Alternatives Considered
- ❌ **Always re-verify (no config gate)**: prod overhead unjustified once D007 worker is keeping drift bounded.
- ❌ **Verify in a background sweep only (no per-request check)**: leaves a window where the user sees broken images.
- ❌ **Trust the cache absolutely**: status quo; produces the very bug this decision exists to fix.
- ❌ **Catch 404 on the `<img>` element only**: addresses the symptom (placeholder) but not the cause (stale cached URL stays cached).

---

## D010: Cart Download — Client-Side ZIP via Manifest Endpoint

**Status**: ✅ Approved  
**Date**: Phase 13 (2026-05-10)  
**Approved By**: User  
**Related**: [D002 Storage Provider Abstraction](#d002-storage-provider-abstraction-layer) • [D005 Storage Path Structure Standard](#d005-storage-path-structure-standard) • [STORAGE_LAYER.md — CORS for SPA Streaming](./STORAGE_LAYER.md#cors-for-spa-streaming-downloads)

### Context

The cart download endpoint historically streamed a server-built ZIP back to the browser: backend pulled each photo from blob storage into a `ZipArchive`, then relayed the bytes to the client. In production this surfaced two compounding problems:

1. **`AllowSynchronousIO` failure** — `ZipArchive.Dispose` performs synchronous writes; ASP.NET Core 9 disallows this by default. Working around it with `AllowSynchronousIO=true` re-introduces a thread-pool starvation risk.
2. **Bandwidth doubled** — every byte travels storage → backend → browser. With Original-quality variants (PR-B added), a 20-photo cart can be 400+ MB; the backend becomes a relay with no value-add.

### Decision

Replace server-streamed ZIP with a **manifest endpoint + client-side ZIP**:

- Backend returns `POST /api/code/{code}/cart/manifest` → `[{ filename, url, sizeBytes? }]`, where each `url` is a short-lived (default 15 min) presigned URL pointing at the chosen-quality blob (`original.jpg`, `medium.jpg`, etc.).
- Frontend uses [`client-zip`](https://github.com/Touffy/client-zip) (~2 KB, streaming, returns a `ReadableStream`) to assemble the ZIP locally from the presigned URLs.
- Save flow:
  - **Chromium** — `showSaveFilePicker()` → write the stream directly to the user's chosen path; constant memory.
  - **Firefox / Safari** — fall back to `new Blob([stream])` + `<a download>`; the whole archive lands in memory before save.

Backend logs one `Download` analytics row per item the manifest returned (URL issued = intent to download).

### Rationale

- **Eliminates server bandwidth relay** — bytes go storage → browser directly. Backend touches metadata only.
- **Removes sync-IO ceremony** — no `ZipArchive.Dispose`, no `AllowSynchronousIO`, no thread-pool risk.
- **Storage layer scales independently** — MinIO / Azure Blob serves the bytes at their own throughput; backend memory stays flat regardless of cart size.
- **Stateless** — no per-request server allocation tied to download duration; presigned URLs are the only handoff.

### Rejected Alternatives

- ❌ **Keep server-streamed ZIP** — already broken in prod with the `AllowSynchronousIO` failure; even if patched, doesn't fix the bandwidth-doubling cost or memory ceiling at large cart sizes.
- ❌ **Server zips once and stores the archive in blob storage, returns one URL** — would generate hundreds of duplicate ZIPs over time (every cart variant produces a unique archive). Unbounded storage growth with no clear retention policy. Also keeps the `ZipArchive.Dispose` problem at upload time rather than download time.
- ❌ **Server-Sent Events / WebSocket-driven progress instead of `client-zip`** — extra protocol surface, doesn't change the bandwidth story, and modern browsers' streaming `fetch` + `client-zip` already deliver the progress UX with no extra plumbing.

### Risks

- **Firefox / Safari memory ceiling** — without `showSaveFilePicker`, the entire ZIP must fit in memory before save. For Original quality this can mean 400+ MB. The frontend warns the user when total manifest size exceeds a threshold (`~250 MB`) on a non-Chromium browser.
- **Presigned URL TTL vs slow downloads** — default 15-minute TTL. A user on a slow connection downloading a multi-GB Original-quality cart may see URLs expire mid-stream. The cart panel offers a "retry" that re-fetches the manifest (issuing fresh URLs). TTL is configurable per environment if needed.
- **CORS bootstrap required** — direct `fetch(url) → ReadableStream` from the SPA origin is a CORS request. MinIO (dev) and Azure Storage (prod) must both be configured to allow the SPA origin. See [STORAGE_LAYER.md — CORS for SPA Streaming Downloads](./STORAGE_LAYER.md#cors-for-spa-streaming-downloads).

### Implementation

- **Backend** — `POST /api/code/{code}/cart/manifest` controller action; `ZipDownloadService` removed; one `Download` row inserted per manifest item.
- **Frontend** — `CartDownloadService` wraps manifest-fetch + `client-zip` + save flow; emits `{ phase: 'manifest' | 'streaming' | 'saving', bytesWritten?, totalBytes? }` progress events to the cart panel.
- **Infra** — `scripts/setup-minio-cors.ps1` bootstraps the dev MinIO bucket; Azure prod uses `az storage cors add` (documented in STORAGE_LAYER.md).

(See PR-F: `cart-download-manifest` for the full implementation.)

### Tests

- Backend — manifest controller test asserts every cart item produces one `{ filename, url }` entry, one `Download` row inserted, and chosen-quality routing (Original → `original.jpg`).
- Frontend — `CartDownloadService` unit test mocks `fetch` with a `ReadableStream`, asserts manifest is fetched first, each item is fetched, and `client-zip` produces a valid archive header.
- E2E — Playwright probe drives a small (3-photo) cart through the full flow on a Chromium build, verifies the saved file is a valid ZIP via the headless `showSaveFilePicker` shim.

---

## D011: Azure Dev Footprint — AAD-Only Auth, User-Delegation SAS, Key Vault Config

**Status**: Approved · **Date**: 2026-05-10 · **Owner**: pg-platform-engineer

### Context

Before deploying PhotoGallery to Azure App Service / AKS, the team wants to
exercise the storage / database / secrets abstractions against the real Azure
data plane while still running the app from a developer laptop. The dev
footprint must be cheap (~$15-20/mo), reproducible via Terraform, and use
**no static secrets on disk**.

### Decision

Provision a minimal dev footprint via `terraform/dev/`:

| Resource | SKU | Why |
|----------|-----|-----|
| Storage Account | Standard_LRS, Hot, StorageV2 | Cheap; no GRS for dev. Account keys **disabled** (`shared_access_key_enabled = false`). |
| Blob container `photogallery` | private | All access via user-delegation SAS. |
| Azure SQL Server + DB | S0 (10 DTU, 250 GB cap, 10 GB allocated) | Basic was rejected (2 GB, awkward AAD ergonomics). |
| Key Vault | Standard, **RBAC mode** | Modern; managed identities + AAD groups instead of legacy access policies. |
| Log Analytics + App Insights | PerGB2018, workspace-based | Free at dev volume. Future-proofs Serilog → AI sink. |

Auth model — **everything goes through `DefaultAzureCredential`**:

1. **Storage**: dev principal gets `Storage Blob Data Contributor` +
   `Storage Blob Delegator`. The app issues **user-delegation SAS** for
   client-readable photo URLs. **Account-key SAS is rejected**: it requires
   us to read the account key (we've disabled that), it can't be revoked
   per-token, and it doesn't work with managed identity in prod — so the dev
   path would diverge from the prod path. User-delegation SAS works
   identically locally (via `az login`) and in App Service (via Workload
   Identity).
2. **SQL**: AAD-only authentication (`azuread_authentication_only = true`).
   SQL admin login is disabled. Connection string uses
   `Authentication=Active Directory Default`, which `DefaultAzureCredential`
   resolves from `az login`. Firewall = Azure-services rule + single dev IP.
3. **Key Vault**: dev principal gets `Key Vault Secrets User` +
   `Key Vault Secrets Officer`. App pulls all secrets at startup via
   `AddAzureKeyVault(uri, new DefaultAzureCredential())`.

Secret-naming convention in Key Vault uses `--` as the path separator
(ASP.NET Core's KV config provider maps `--` → `:`):

```
Sql--ConnectionString          → Sql:ConnectionString
Storage--AccountName           → Storage:AccountName
Storage--BlobEndpoint          → Storage:BlobEndpoint
Storage--ContainerName         → Storage:ContainerName
Auth--Google--ClientId         → Auth:Google:ClientId
Auth--Google--ClientSecret     → Auth:Google:ClientSecret
Auth--Jwt--SigningKey          → Auth:Jwt:SigningKey
Acs--ConnectionString          → Acs:ConnectionString
```

State backend: a separate, manually-bootstrapped `rg-photogallery-tfstate`
resource group with a versioned, soft-delete-enabled Storage Account holding
the `tfstate` container. Bootstrap is a PowerShell script
(`terraform/bootstrap/bootstrap-state.ps1`), not Terraform — chicken-and-egg.
Backend uses `use_azuread_auth = true`, so even Terraform itself doesn't read
storage keys.

### Consequences

**Positive**

- Local dev path matches future prod path (managed identity → AAD → resources).
  No "works on my machine because I have the account key" surprises.
- Zero connection strings, account keys, or shared secrets in source control,
  appsettings, or env vars.
- Modules in `terraform/modules/{storage,sql,keyvault,observability}` are
  reusable for `terraform/prod/` later — only SKUs and network posture differ.

**Negative / follow-ups**

- The current `PhotoGallery/Services/Storage/AzureStorageProvider.cs` reads a
  connection string and uses `BlobClient.GenerateSasUri(...)`, which produces
  an **account-key SAS**. This is incompatible with our
  `shared_access_key_enabled = false` decision. **Backend follow-up**:
  refactor to construct `BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential())`
  and replace `GenerateSasUri` with a user-delegation SAS issued via
  `BlobServiceClient.GetUserDelegationKeyAsync(...)` + a `BlobSasBuilder`.
- First Azure SQL S0 cold start on a fresh DB takes 30-60s for
  `Database.Migrate()`. Acceptable for dev; document in runbook.
- Soft-delete + 7-day retention costs cents but means we can't immediately
  reuse a deleted blob name. Fine for dev.

### Cost guard

| Resource | Monthly |
|----------|---------|
| Storage (a few GB, Hot, LRS) | <$1 |
| Azure SQL DB S0 | ~$15 |
| Key Vault standard | <$1 |
| Log Analytics + App Insights (under free tier) | $0 |
| **Total** | **~$17** |

### References

- `terraform/` — modules + dev composition
- `Documentation/Runbooks/local-azure-dev.md` — developer workflow
- Microsoft docs: [User Delegation SAS](https://learn.microsoft.com/azure/storage/common/storage-sas-overview#user-delegation-sas) (preferred over account-key SAS)
- Microsoft docs: [Key Vault RBAC](https://learn.microsoft.com/azure/key-vault/general/rbac-guide)

---

## D012: Single `PhotoGallery`-Prefixed Resource Group for Dev Footprint (incl. tfstate)

**Status**: Approved · **Date**: 2026-05-11 · **Owner**: pg-platform-engineer

### Context

D011 originally provisioned two RGs: `rg-photogallery-tfstate` (state backend,
bootstrapped manually) and `rg-photogallery-dev` (workload, managed by
Terraform). The product owner has since stated two non-negotiable constraints
for the dev subscription `4fc243fa-5de2-48cb-9c98-793701d13152`:

1. **Every** resource group name must start with `PhotoGallery`.
2. The dev footprint must live in a **single** resource group.

The naming convention is straightforward (cosmetic rename). The "single RG"
constraint conflicts with the chicken-and-egg of Terraform's own state
backend: Terraform cannot create the storage account that holds its state.

### Decision

**One resource group: `PhotoGallery-dev`.** It holds both the Terraform state
storage account and every workload resource (Storage, SQL, Key Vault,
observability).

Ownership split:

| Resource | Created by | Managed by Terraform? |
|----------|------------|------------------------|
| `PhotoGallery-dev` RG | `terraform/bootstrap/bootstrap-state.ps1` | **No** — adopted via `data "azurerm_resource_group"` |
| `stpgtfstate<hash>` state SA | bootstrap script | **No** — managed out of band |
| `tfstate` container | bootstrap script | **No** |
| Workload Storage Account, SQL, Key Vault, observability | `terraform/dev/main.tf` | **Yes** |

Bootstrap flow:

1. `bootstrap-state.ps1` runs `az group create --name PhotoGallery-dev` and
   creates the state SA + container inside it. Idempotent.
2. `terraform/dev/main.tf` declares `data "azurerm_resource_group" "this" { name = var.resource_group_name }`
   (default `PhotoGallery-dev`) and points every module at
   `data.azurerm_resource_group.this.name`. Terraform never tries to create
   or destroy the RG.
3. `terraform destroy` cleans only the workload resources Terraform created.
   The RG and state SA survive — exactly the lifecycle we want.

The `subscription_id` variable defaults to the pinned dev sub
(`4fc243fa-5de2-48cb-9c98-793701d13152`) and is set on the `azurerm` provider
block. `var.resource_group_name` has a validation rule rejecting any value
that doesn't start with `PhotoGallery`.

### Alternatives considered

- **Two RGs, both `PhotoGallery`-prefixed** (`PhotoGallery-tfstate` +
  `PhotoGallery-dev`). Satisfies the naming rule but violates "single RG."
  Rejected.
- **One RG, Terraform manages it** (no data source; `terraform import` after
  bootstrap). Adds a fragile one-time import step that's easy to forget on
  fresh machines. Also means `terraform destroy` would try to delete the RG
  containing its own state SA — Terraform will refuse, leaving a confusing
  half-destroyed state. Rejected.
- **Single RG, Terraform manages it, state in a different sub.** Would
  satisfy both constraints inside the dev sub but introduces cross-sub state
  ownership that's overkill for a ~$17/mo dev footprint. Rejected.

### Consequences

**Positive**

- Literal compliance with the owner's "single RG, `PhotoGallery`-prefixed"
  rule.
- `terraform destroy` is safe: it never targets the RG or the state SA, so
  state survives a full workload teardown.
- One mental model for resource location: "everything PhotoGallery dev lives
  in `PhotoGallery-dev`."

**Negative / follow-ups**

- The RG itself is not codified in Terraform. Tags / locks / policies on the
  RG are managed by the bootstrap script, not by `terraform apply`. If RG-level
  configuration grows, revisit (likely answer: add an `azurerm_management_lock`
  to the bootstrap script rather than Terraform).
- Two artifacts (bootstrap script + Terraform) both write into the same RG;
  reviewers must keep them aligned. Mitigated by validation: `var.resource_group_name`
  must start with `PhotoGallery`, and the bootstrap script enforces the same
  prefix check.
- A fully clean reset requires `az group delete --name PhotoGallery-dev` after
  `terraform destroy`. Documented in the runbook.

### References

- `terraform/bootstrap/bootstrap-state.ps1` — creates RG + state SA
- `terraform/dev/main.tf` — adopts RG via `data` source
- `terraform/dev/variables.tf` — `subscription_id` and `resource_group_name` defaults + validation
- `Documentation/Runbooks/local-azure-dev.md` — updated bootstrap + teardown flow

---

## D013: Cheapest Dev SKUs — SQL Basic + Container Apps Consumption (scale-to-zero) + ghcr.io

**Status**: Approved · **Date**: 2026-05-12 · **Owner**: pg-platform-engineer

### Context

D011 stood up the Azure dev footprint with Standard S0 SQL (~$15/mo) and no
compute resource — the API ran locally against the cloud data plane. The
product owner subsequently asked for two things:

1. The **cheapest viable** Azure SQL SKU that still supports EF Core
   migrations and AAD-only auth.
2. A **cheapest viable** Azure compute target for the eventual API
   container, provisionable now (placeholder image OK) so MI / KV / SQL
   wiring is in place when pg-devops-cicd publishes the real image.

Constraints carried forward from D012: single `PhotoGallery-dev` resource
group, pinned subscription `4fc243fa-5de2-48cb-9c98-793701d13152`, all
resources `PhotoGallery*`-prefixed where naming permits.

### Decision

**SQL: Basic (5 DTU, 2 GB) — flat ~$5/mo.**

| Option | Cost | Verdict |
|--------|------|---------|
| **Basic (5 DTU, 2 GB)** | **~$5/mo flat** | **Chosen.** Simplest pricing. Supports AAD-only auth (since 2022) and EF Core migrations. 2 GB cap is plenty for MVP metadata (album/photo rows, users, carts measured in MB, not GB). |
| Standard S0 (10 DTU, 250 GB) | ~$15/mo flat | Previous default. Rejected: the only thing it bought us over Basic was headroom we don't need yet, at 3× the cost. Trivial bump to S0 later via `sql_sku_name`. |
| GP_S_Gen5_1 Serverless w/ auto-pause (60 min) | ~$1.50/mo idle (storage only) + ~$0.52/vCore-hr active | Rejected as default. Wins *only* if the DB is truly idle for weeks. For routine dev (a few hours/week of activity), it lands above Basic on cost AND adds 30-60 s cold-start pain on every wake. Documented as a "switch to this if you stop dev'ing for months at a time" alternative. |

**Compute: Azure Container Apps, Consumption plan, scale-to-zero
(`min_replicas = 0`, `max_replicas = 1`, 0.5 vCPU / 1 GiB).**

| Option | Cost (idle) | Verdict |
|--------|-------------|---------|
| **ACA Consumption, scale-to-zero** | **~$0/mo idle** (per-request execution charges only, sub-cent at MVP) | **Chosen.** Idle cost effectively zero. Cold-start ~1-3 s. Supports KV secret references via managed identity, external HTTPS ingress, full revision/rollback. |
| App Service B1 | ~$13/mo flat (always-on) | Rejected. 2× the entire current cost guard for "always warm" we don't need. |
| ACA Dedicated workload profile | ~$70/mo+ flat | Rejected. Production-only consideration. |
| AKS | ~$70/mo+ for the cluster alone | Rejected. Massively over-spec for a single API container. |

**Registry: ghcr.io (free for public images), defer ACR.**

PhotoGallery (`ArmyGuy255A/PhotoGallery`) is public on GitHub. **GitHub
Container Registry** publishes the backend image as a public package at
zero cost, with anonymous pull. Container Apps pulls anonymously — no
registry credential block, no extra Terraform resource. **ACR Basic
($5/mo)** is deferred until private images become a requirement (e.g., we
add a closed-source paid tier or want signed-only images). Documented in
the runbook.

**Identity: user-assigned MI (UAMI), not system-assigned.**

The container app's KV secret references resolve at revision-deploy time.
With a system-assigned MI, the `principal_id` is only known *after* the
container app exists, which creates a chicken-and-egg with the
`Key Vault Secrets User` role assignment. UAMI breaks the cycle:

1. Create UAMI (principal_id known immediately).
2. Assign KV / Storage roles to UAMI's principal.
3. `time_sleep` 60 s for AAD propagation.
4. Create container app, attach UAMI, declare KV secrets with
   `identity = <uami id>` — secrets resolve cleanly on first revision.

The UAMI is also the SQL principal: a manual one-time T-SQL step
(`CREATE USER [<uami-name>] FROM EXTERNAL PROVIDER` + `db_datareader` /
`db_datawriter` / `db_ddladmin`) registers the UAMI in the DB. Terraform
deliberately does not run T-SQL — keeping that out of the IaC plane avoids
brittle null-resource wrappers. The runbook walks the developer through it.

**Frontend hosting: out of scope for this pass.** Cheapest viable path
will be **Azure Static Web Apps Free tier** (0 $/mo, generous bandwidth,
built-in SSL). Tracked as a follow-up.

### Cost summary (idle)

| Resource | SKU | $/mo |
|----------|-----|------|
| Storage Account | Standard_LRS, Hot | <$1 |
| Azure SQL Database | **Basic** | **~$5** |
| Key Vault | Standard | <$1 |
| Container Apps Env + App | **Consumption, scale-to-zero** | **~$0** idle |
| Log Analytics + App Insights | first 5 GB free | $0 |
| Container registry | ghcr.io public package | $0 |
| **Total dev footprint, idle** | | **~$6–7/mo** |

Down from ~$17/mo under D011 — a ~60% reduction with no loss of
functionality for the MVP scope.

### Alternatives considered (and rejected)

- **SQL Serverless (GP_S) as the default.** Rejected — see table above.
  Documented as a fallback for very-idle developers.
- **System-assigned MI on the container app.** Rejected — chicken-and-egg
  with KV role assignment forces apply-twice or a fragile
  `null_resource` workaround. UAMI is the standard pattern.
- **Embed image-pull credentials for ACR Basic.** Rejected — needless
  complexity and $5/mo for a public OSS project. ghcr.io public package is
  free.
- **App Service B1 with always-on health probe.** Rejected — flat $13/mo
  beats by ACA's near-zero idle cost handily for an MVP that's mostly idle.
- **Provision frontend hosting in this pass.** Deferred — keeps this PR
  scoped; SWA Free tier needs a custom-domain decision separately.

### Consequences

**Positive**

- Idle cost ~$6–7/mo — enables long-lived dev environments without budget
  pressure.
- ACA's scale-to-zero pairs naturally with SQL Basic: both go quiet when
  the developer isn't poking at them.
- Terraform-managed UAMI + RBAC means re-creating the API resource is one
  `terraform apply` away. The only manual step is the SQL `CREATE USER`.

**Negative / follow-ups**

- 2 GB SQL cap is a real limit. The runbook calls out the exact override
  (`sql_sku_name = "S0"`) for when seed/migration data starts crowding it.
- Cold-start on ACA (~1-3 s) and SQL Basic (~30-60 s after long idle)
  combine to a worst-case ~minute on the *very first* request after a
  long quiet period. Acceptable for dev; documented in troubleshooting.
- First `terraform apply` may race AAD role propagation for the UAMI's
  KV secret resolution. Mitigated with `time_sleep 60s` + a runbook note
  ("re-run apply if the first one fails with 403"). The retry succeeds
  cleanly because the role is already in place.
- The SQL `CREATE USER ... FROM EXTERNAL PROVIDER` step is genuinely
  out-of-band. If a developer destroys + re-creates the DB, they must
  redo it. Tracked in the runbook (step 3a).
- ghcr.io public package leaks the image bytes (image is open source
  anyway). When PhotoGallery adds private functionality, revisit and add
  an ACR Basic module (~$5/mo).

### References

- `terraform/modules/sql/` — SKU default flipped to `Basic`, `max_size_gb = 2`
- `terraform/modules/compute/` — new module: ACA env + container app + UAMI + role assignments + `time_sleep`
- `terraform/dev/main.tf` — wires `module.compute` after `module.observability`
- `terraform/dev/variables.tf` — `container_app_image` (defaults to MCR placeholder), `container_app_target_port` (8080)
- `Documentation/Runbooks/local-azure-dev.md` — step 3a (SQL UAMI registration), updated cost table, ACA troubleshooting

### Addendum (2026-05-13) — Key Vault secret naming contract

The KV secret names follow the .NET `:`-config-path convention with `--`
substituted for `:` (the ASP.NET Core Key Vault config provider's translation
rule). ACA bridges each secret to a container env var that uses `__` (double
underscore) for the same separator. All three forms — `--` in KV, `-` in ACA
secret alias, `__` in container env var — collapse to a single canonical .NET
config path at runtime.

The locked table (canonical names + .NET paths) lives in
`Documentation/Runbooks/local-azure-dev.md` ("Key Vault secret contract"),
and `PhotoGallery/ConfigurationCanonicalAliases.cs` is the .NET-side source
of truth. Terraform's `azurerm_key_vault_secret` resources and ACA env-var
mappings in `terraform/modules/keyvault/main.tf` and `terraform/dev/main.tf`
must match that table exactly.

---

## D014: Pivot from ghcr.io to Azure Container Registry (Basic SKU) for Dev

### Status

Approved — implemented 2026-05-10 on `u/copilot/feat/azure-dev-integration`.

### Context

D013 (above) shipped the dev compute tier with the API image hosted on
**ghcr.io as a public package** (free), pulled anonymously by Azure Container
Apps. That kept the registry line item at $0 and had a simple "open source
anyway" story.

Two pressures changed the calculus:

1. **Single billing/access boundary.** Every other dev-tier resource lives in
   the `PhotoGallery-dev` resource group on subscription
   `4fc243fa-…-d13152` (per D012). Splitting the image tier out to GitHub
   means a separate IAM model (GitHub PATs / OIDC), separate audit trail,
   and a separate place to revoke access on offboarding.
2. **AAD-only credential story.** The rest of the footprint authenticates
   over AAD (UAMI for the app; developer principal via `az login`). The
   ghcr.io path forced us to either accept anonymous public pulls *or*
   provision a long-lived PAT secret in ACA's registry block. Neither
   matches the "no shared keys" posture from D011.

### Decision

Add an **Azure Container Registry, SKU `Basic`** to the dev footprint:

- Name: `acrpgdeva4pi` (lowercase alnum, ACR's globally-unique constraint).
- `admin_enabled = false` — no shared username/password ever issued.
- ACA pulls authenticate via the existing **UAMI** (`AcrPull` role on the
  registry). Wired in the composition root, not the compute module, to
  avoid a circular dep between `modules/compute` and `modules/acr`.
- Developer pushes use **AAD** (`az acr login` against the AAD-backed flow,
  then `docker push`). The developer principal holds **`AcrPush`** on the
  registry — assigned in `dev/main.tf` against `var.dev_principal_object_id`.
- ACA's `registry { server, identity }` block is added behind a new optional
  input `container_registry_server` on the compute module. Empty string =
  no registry block (preserves the D013 anonymous-MCR-placeholder path for
  any future env that wants it).

### Consequences

**Pros**

- Single RG/sub for billing, RBAC, audit, and offboarding.
- No long-lived registry credentials anywhere — pulls and pushes are both
  AAD, end-to-end. Matches D011's posture.
- Image tier survives if the GitHub org / package is deleted or the PAT is
  rotated; coupled to Azure tenant lifecycle instead.
- ACR Tasks available later (build-on-push, vuln scanning hookup) without
  another migration.

**Cons / Trade-offs**

- **+$5/mo** flat. Dev footprint goes from ~$6-7/mo idle to ~$11-12/mo idle.
  Acceptable for MVP; documented in `terraform/README.md` cost table.
- **Lose ghcr.io's free public-image story.** Anyone wanting to pull the
  image now needs an AAD identity with `AcrPull` on `acrpgdeva4pi`. For an
  open-source project this is friction; mitigated by the fact that the
  source is on GitHub and a cold rebuild is `docker build`.
- **Basic SKU has no private endpoint** (Premium-only). Network access stays
  public; AAD does the auth. Acceptable for dev.
- No geo-replication on Basic. Acceptable — single-region dev only.

### Notes

- The compute module's `image` attribute remains in `lifecycle.ignore_changes`
  (per D013), so CI/CD can flip the running image (`acrpgdeva4pi.azurecr.io/
  photogallery-backend:<tag>`) without fighting Terraform.
- When prod ships, revisit SKU. Premium ($1.67/day ≈ $50/mo) buys
  geo-replication, private link, and content trust — likely worth it for
  prod, overkill for dev.

### References

- `terraform/modules/acr/` — new module (azurerm_container_registry, Basic, admin off)
- `terraform/dev/main.tf` — wires `module.acr`, adds `azurerm_role_assignment.aca_acr_pull` and `dev_acr_push`, plumbs `container_registry_server` into compute
- `terraform/modules/compute/` — new optional `container_registry_server` variable, dynamic `registry` block on the container app authenticated via the UAMI
- `terraform/dev/outputs.tf` — `container_registry_name`, `container_registry_login_server`
- `Documentation/Runbooks/local-azure-dev.md` — "Container Registry" section (push/pull workflow)

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

## D015: GitHub Actions → ACR via Workload Identity Federation (OIDC)

**Status**: Approved · **Date**: 2026-05-10 · **Owner**: pg-devops-cicd

### Context

D014 stood up ACR with `admin_enabled = false` — pushes from a developer
laptop authenticate via AAD (`az acr login` + AcrPush on the dev principal).
The CI/CD pipeline (`.github/workflows/build.yml`) builds the backend and
frontend images on every push to `main` but had `push: false` because no
trusted identity was wired up to ACR. We need merges to `main` to publish
`latest` + `<git-sha>` tags to `acrpgdeva4pi.azurecr.io` so ACA can pick
up the new revision.

### Decision

Use **GitHub OIDC + Azure AD Workload Identity Federation** instead of a
client-secret service principal:

1. Provision a dedicated AAD application + service principal per environment
   (`photogallery-github-actions-dev`) via the new `terraform/modules/github_oidc`
   module.
2. Register a federated identity credential trusting GitHub's OIDC issuer
   (`https://token.actions.githubusercontent.com`) with the subject
   `repo:ArmyGuy255A/PhotoGallery:ref:refs/heads/main`. Only pushes on
   `main` (i.e. merged PRs) can assume the SP.
3. Assign **AcrPush** to that SP scoped to the ACR resource — nothing else.
4. CI uses `azure/login@v2` with `client-id`/`tenant-id`/`subscription-id`
   from GitHub **repo variables** (not secrets — these are non-sensitive
   identifiers), then `az acr login`, then `docker/build-push-action@v5`
   with `push: true`.

### Rationale

- **No long-lived secrets.** GitHub issues a short-lived OIDC token per job;
  Azure AD trades it for an access token scoped to AcrPush. Nothing to
  rotate, nothing to leak in logs.
- **Least privilege.** The SP holds only AcrPush on a single registry, and
  the federated credential's `subject` claim prevents fork PRs or other
  branches from assuming it.
- **Idempotent.** All of it is Terraform-managed; tearing down the
  environment removes the trust automatically.

### Implementation

- `terraform/modules/github_oidc/` — reusable module (app + SP + N federated
  credentials via `for_each` over a `subjects` map).
- `terraform/dev/main.tf` — instantiates the module with the `main` subject
  and assigns AcrPush against `module.acr.id`.
- `terraform/dev/outputs.tf` — surfaces `github_actions_client_id`,
  `tenant_id`, `subscription_id`, `container_registry_login_server`.
- `.github/workflows/build.yml` — `build-docker` job adds
  `permissions: id-token: write`, logs in via `azure/login@v2`, runs
  `az acr login`, and pushes both images with `latest` + `${{ github.sha }}`
  tags.

### Post-apply runbook

After `terraform apply` in `terraform/dev`:

```bash
gh variable set AZURE_CLIENT_ID       --body "$(terraform output -raw github_actions_client_id)"
gh variable set AZURE_TENANT_ID       --body "$(terraform output -raw tenant_id)"
gh variable set AZURE_SUBSCRIPTION_ID --body "$(terraform output -raw subscription_id)"
gh variable set ACR_LOGIN_SERVER      --body "$(terraform output -raw container_registry_login_server)"
```

### Alternatives considered

- **Service principal with client secret** — rejected; long-lived credentials
  in a GitHub secret violate the "no secrets in Key Vault" spirit of D014
  and require manual rotation.
- **ACR admin user + push token** — rejected; admin user is explicitly
  disabled in the ACR module per D014.
- **GitHub-hosted ACR push token via `azure/docker-login`** — same problem
  as service-principal secrets.

---

**Last Updated**: 2026-05-10  
**Total Decisions**: 12 (D001-D008, D010, D011, D014, D015; D009 lives in [docs/decisions/D009-watermark-pipeline.md](../../docs/decisions/D009-watermark-pipeline.md))  
**All Approved**: ✅ Yes  
**In Implementation**: Phases 1-12 (D001-D005); Phase 13 in progress (D006-D008, D010)

## D015: Angular Frontend Hosted on Azure Static Web Apps (Free Tier)

**Status:** ✅ Accepted (2026-05-10)
**Decision owner:** pg-platform-engineer
**Related:** D011 (Azure dev footprint), D012 (single RG), D014 (ACR pivot)

### Context

The PhotoGallery dev footprint already provisions the API (Container Apps),
data plane (Azure SQL + Storage + Key Vault), and observability tier in the
single `PhotoGallery-dev` resource group. The Angular SPA has been running
only locally (`ng serve` against the cloud API). To validate the
production-shaped network path (cross-origin SPA → API on a different
hostname, SSL termination, etc.) and to give reviewers a clickable preview
URL, we need a cloud host for the FE that costs ~$0 and slots into the
existing single-RG model.

### Decision

Add **Azure Static Web Apps, Free tier** as a new Terraform module
(`terraform/modules/staticwebapp`) wired from `terraform/dev/main.tf`. The
SWA lives in `PhotoGallery-dev` (preserves D012). The Angular build/deploy
flow is owned by the FE dev's GitHub Actions workflow, which reads the
SWA `api_key` Terraform output as a repo secret.

The API's CORS allowlist is extended via injected ACA env vars so the
deployed SPA's origin is accepted:

- `Frontend__Url` ← `module.staticwebapp.default_host_url` (existing
  single-origin CORS path)
- `Cors__AllowedOrigins__0` ← same (new `List<string>` binding path)
- `Cors__AllowedOrigins__N` ← `var.frontend_origin_extra[N-1]` (e.g.
  `http://localhost:4200` for dev-server-against-cloud-API)

A small backend change in `Configuration/ConfigurationSettings.cs` and
`PhotoGallery/Program.cs` unions `Frontend.Url` with `Cors.AllowedOrigins`,
deduplicates, and feeds `WithOrigins(...)` so multi-origin support is real
(not just first-slot).

### Rationale

- **$0/mo.** SWA Free includes 100 GB/mo bandwidth, free SSL on the
  `*.azurestaticapps.net` hostname AND on user-provided custom domains,
  staging slots, and a built-in GitHub Actions deploy story. Total dev
  footprint stays ~$11-12/mo.
- **Same RG (single billing/access boundary).** Preserves D012.
- **First-class GH Actions deploy.** `Azure/static-web-apps-deploy@v1` is
  the canonical pattern; the FE dev's workflow is one job, one secret.
- **Custom domain story works today.** When PhotoGallery gets a real
  domain, SWA wires it up with a CNAME + free managed cert. No HTTPS
  scramble at prod cutover.

### Alternatives considered

- **Azure Storage Account static website** — cheaper still (~$0.10/mo),
  but no free SSL on a custom domain (you have to front it with Front Door
  or Cloudflare, which adds cost and ops surface). Free SWA wins.
- **App Service (Free F1 or Basic B1)** — overkill for a static SPA;
  Free F1 has 60 CPU min/day caps and no SSL on custom domains; B1
  doubles the dev cost. Rejected.
- **Cloudflare Pages** — excellent product, but lives outside the Azure
  subscription. Violates the single-RG / single-bill model from D012 and
  fragments auth (separate Cloudflare API token vs. AAD). Rejected for
  dev; could revisit for prod CDN.

### Trade-offs

- **CORS allowlist update requires `terraform apply`** (option A in the
  task brief). Acceptable: bouncing the ACA revision is seconds, and
  origin churn is rare. If/when it gets painful, move to a KV-backed
  semicolon-separated list and read it dynamically.
- **No private endpoint on Free tier.** Acceptable for dev; the public
  surface is just the static assets. For prod, Standard tier ($9/mo)
  adds private endpoints + managed identities + larger size caps —
  reconsider when prod ships.

### Notes

- SWA Free is region-restricted (eastus2, centralus, westus2, westeurope,
  eastasia). Module enforces this via a `validation` on `var.location`.
  Default `eastus2` co-locates with the rest of the dev footprint.
- The SWA → compute `BACKEND_API_URL` app setting was intentionally left
  unwired by Terraform to avoid a SWA ↔ compute dependency cycle (compute
  already references SWA via the CORS allowlist). The FE deploy GH Action
  populates it out of band with `az staticwebapp appsettings set`.

### References

- `terraform/modules/staticwebapp/` — new module
- `terraform/dev/main.tf` — adds `module.staticwebapp`, injects
  `Cors__AllowedOrigins__N` and `Frontend__Url` into compute env
- `terraform/dev/variables.tf` — `frontend_origin_extra` (default `[]`)
- `terraform/dev/outputs.tf` — `static_web_app_default_host_name`,
  `static_web_app_url`, `static_web_app_api_key` (sensitive)
- `Configuration/ConfigurationSettings.cs` — new `Cors` class with
  `List<string> AllowedOrigins`
- `PhotoGallery/Program.cs` — multi-origin union for `AllowFrontendDev`
- `Documentation/Runbooks/local-azure-dev.md` — "Frontend hosting" section


---

## D016: Release-Driven Deployment — `trial` → `main` cuts the next `A.B.C.D` Release

**Status:** ✅ Accepted (2026-05-10)
**Decision owner:** pg-devops-cicd
**Related:** D013 (compute), D014 (ACR), D015 (OIDC), D015-SWA (frontend hosting)

### Context

Through D015 we had every merge into `main` automatically build + push images to ACR and roll a new ACA revision; the Angular SPA deployed to SWA on every FE-touching merge to `main`. That's "main = production". It worked, but it collapsed two ideas that should be separate:

1. **"Code is merged"** — reviewed, tested, integrated.
2. **"Code is released"** — a deliberate, versioned, deployable snapshot.

With the API exposed at the cloud edge and a real DB behind it, conflating those two means every speculative merge ships to prod. There's no labelled artifact to anchor rollback to, no versioned changelog, and no breathing room between integration and exposure.

### Decision

Split the lifecycle:

```
feature branch  ─PR─▶  trial  ──PR (only allowed source)──▶  main  ──tag + release──▶  deploy
   (u/<actor>/<type>/<scope>)        (default work)                  (release-only)         (ACR + ACA + SWA)
```

- **`trial`** is the integration branch. All feature branches PR into it. CI (build + test + lint + vuln scan) runs on push + PR. **No publish, no deploy.**
- **`main`** is release-only and protected: the *only* allowed source for an incoming PR is `trial`. No direct pushes. Every merge into `main` auto-cuts a release.
- Releases are versioned `A.B.C.D` (first release: `1.0.0.0`), where the segment to bump is picked from the merging PR's labels:
  - `release:major`    → bumps **A** (resets B/C/D)
  - `release:minor`    → bumps **B** (resets C/D)
  - `release:revision` → bumps **C** (resets D)
  - `release:patch`    → bumps **D** (also the default if no `release:*` label is present)
- The `release: published` event on the GitHub Release drives the actual build + deploy. ACR images get **three** tags per push: `:<version>`, `:<sha>`, `:latest`. ACA points at the version tag so rollback is `az containerapp update --image ...:vA.B.C.D` against a prior version.

### Workflow architecture

| Workflow file | Trigger | What it does |
|---|---|---|
| `.github/workflows/build.yml` | push/PR `trial` + `main` | Backend tests, frontend tests, warning audit, vuln scan. **No** image build, **no** deploy. |
| `.github/workflows/cut-release.yml` | push `main` | Reads merging PR's `release:*` label → bumps `A.B.C.D` → annotated tag + `gh release create --generate-notes`. |
| `.github/workflows/release.yml` | `release: { types: [published] }` | Checkout release tag → build & push backend + frontend ACR images with 3 tags each → `az containerapp update` to the version tag → deploy SPA bundle to SWA. |
| `.github/workflows/validate-pr-base.yml` | `pull_request: { branches: [main] }` | Fails the check unless `head.ref == 'trial'`. Required status check on `main`. |
| `.github/workflows/deploy-frontend.yml` | — | **Deleted.** Folded into `release.yml`. |

### Branch protection on `main`

Required status checks (set via `gh api PUT repos/.../branches/main/protection`):

- `Backend build + tests`
- `Frontend build + tests`
- `Backend warning audit`
- `Validate PR base`

Plus: `allow_force_pushes = false`, `allow_deletions = false`, `restrictions = null` (no user pinning), `enforce_admins = false` (so the human can emergency-merge), no required reviews (solo repo).

### Hotfix override (emergency escape hatch)

The `trial → main` policy is enforced by the **workflow** `validate-pr-base.yml`, not by branch protection's "restrict source branches" feature, because the workflow can encode a conditional override that GitHub's protection rules can't.

A PR into `main` from any branch **other than** `trial` is allowed if **both** of these are true:

1. The PR carries the label **`hotfix:emergency`**.
2. At least one **APPROVED** review has been submitted by a non-bot reviewer.

If either condition is missing, the check fails with an explanatory message; the merge button stays disabled because `Validate PR base` is a required status check.

The workflow re-runs on `pull_request_review: submitted/dismissed` and `pull_request: labeled/unlabeled`, so adding the label or getting the review flips the check to passing without needing a fresh push.

**When to use it:**

- Production is down and waiting on the next `trial → main` PR is too slow.
- The fix needs to ship under a fresh release tag immediately (the `release:*` label still drives the version bump as normal).

**What it does NOT bypass:**

- Status checks (backend/frontend tests, warning audit).
- The human-review requirement (it's the whole point).
- Force-push / branch-deletion protections.
- The release pipeline (a successful merge still goes through `cut-release.yml` + `release.yml`).

### Rationale

- **Decouples merged from deployed.** A passing PR into `trial` is not a deploy; releases are a deliberate act with a versioned anchor.
- **Versioned rollback.** Every deployed ACA revision is bound to an immutable `:vA.B.C.D` image tag. Rollback is a one-liner.
- **No new long-lived credentials.** Reuses the GitHub OIDC SP from D015 (already holds AcrPush + Contributor on the Container App).
- **Single-source release notes.** `gh release create --generate-notes` collates merged PR titles since the last tag.
- **Cheap to walk back.** If we hate this in a month, deleting `cut-release.yml` + `release.yml` and pointing the deploy bits back at `build.yml` restores the old flow in one PR.
- **Emergency escape hatch with teeth.** The `hotfix:emergency` override bypasses the `trial` source requirement but not the human-review gate. This avoids the common pattern where an "emergency" override silently bypasses *all* safety checks.

### Versioning convention

`A.B.C.D` (not stock SemVer) because PhotoGallery treats "feature" (B) and "refactor of an existing feature" (C) as different shapes of change. The label-driven model lets the human encode intent at PR time without rewriting commit messages or threading semver hints through git history.

Default = `patch` (D). If a PR ships without any `release:*` label, we still produce a release — a bug-fix-sized one. This is the most common case, so labelling is opt-in for anything other than patch.

### One-time setup runbook (post-merge)

```bash
# 1. Create the trial branch off main (if it doesn't exist yet).
git checkout main && git pull
git checkout -b trial && git push -u origin trial

# 2. Create the four release labels (idempotent).
gh label create release:major    --color B60205 --description "Bump A in A.B.C.D" || true
gh label create release:minor    --color D93F0B --description "Bump B in A.B.C.D" || true
gh label create release:revision --color FBCA04 --description "Bump C in A.B.C.D" || true
gh label create release:patch    --color 0E8A16 --description "Bump D in A.B.C.D" || true
gh label create hotfix:emergency --color 5319E7 --description "Bypass trial-source check (still requires 1 approving review)" || true

# 3. Lock down main. PRs must come from trial, status checks required, no force-push.
gh api -X PUT repos/ArmyGuy255A/PhotoGallery/branches/main/protection \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Backend build + tests",
      "Frontend build + tests",
      "Backend warning audit",
      "Source branch must be 'trial'"
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
```

### Alternatives considered

- **Conventional Commits drive the bump** (`feat!`/`BREAKING CHANGE` → major, `feat` → minor, `fix` → patch). Rejected — A.B.C.D's 4-segment scheme doesn't map cleanly to 3-segment SemVer rules, and "revision of an existing feature" is a category we want to track distinctly.
- **release-drafter draft-then-publish.** Rejected for now — adds a step before deploy, more moving parts. `--generate-notes` is good enough; can swap in release-drafter later for richer templates.
- **Manual `gh release create` on every release.** Rejected — easy to forget, and the value of the trial → main merge being the release boundary is exactly that it's automatic.
- **Tag-then-publish on `main` directly, skipping `trial`.** Rejected — the protective value of `trial` is the integration buffer. Without it, the "merged ≠ deployed" idea is enforced only by convention, not by branch policy.

### Implementation

- `.github/workflows/build.yml` — triggers updated to `[trial, main]`; `build-docker` and `deploy-backend` jobs removed (they moved to `release.yml`).
- `.github/workflows/cut-release.yml` — new.
- `.github/workflows/release.yml` — new; replaces `deploy-frontend.yml`.
- `.github/workflows/validate-pr-base.yml` — new.
- `.github/workflows/deploy-frontend.yml` — deleted.
- Skill updates (`skills/*/SKILL.md`) — `branch-strategy-u-prefix` mentions now target `trial`, not `main`.