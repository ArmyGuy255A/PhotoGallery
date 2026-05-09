---
name: photogallery-tdd-unit-testing
description: |
  Test-Driven Development (TDD) expertise for PhotoGallery backend testing. Use this skill BEFORE implementing any backend features, API endpoints, or services. This skill guides the creation of comprehensive xUnit tests that define expected behavior, serve as regression detectors, and validate SOLID principles. Every feature starts with unit tests, then implementation follows to make tests pass.
  
  This skill delegates to copilot-dev-team plugin meta-skills: `aspnet-tdd-xunit` (canonical xUnit + WebApplicationFactory workflow, Arrange-Act-Assert, naming, fakes vs mocks) and `solid-dry-principles` (test-design implications of SOLID). Auto-trigger these when their conditions match. The plugin's `aspnet-tdd-xunit` is canonical — prefer it on conflict.
  
  **CRITICAL: This skill MUST be invoked BEFORE any implementation work begins.**
  
  **Use this skill for:**
  - Designing test cases for new features (entities, repositories, services, endpoints)
  - Writing xUnit/Moq tests in PhotoGallery.Tests project
  - Testing business logic in domain entities
  - Testing API endpoints with mock dependencies
  - Testing storage providers (MinIO abstraction)
  - Testing image processing services
  - Testing authentication/authorization flows
  - Validating error handling and edge cases
  
  **Related skills that depend on this:**
  - **backend-developer** - MUST consult TDD before writing any code
  - **photogallery-architect-skill** - Tests validate SOLID/DRY compliance

  This skill delegates to copilot-dev-team plugin meta-skills: `aspnet-tdd-xunit` (canonical xUnit + WebApplicationFactory workflow, Arrange-Act-Assert, naming, fakes vs mocks) and `solid-dry-principles` (test-design implications of SOLID). Auto-trigger these when their conditions match. The plugin's `aspnet-tdd-xunit` is canonical — prefer it on conflict.
---

# Test-Driven Development (TDD) Skill: PhotoGallery Backend Testing

## Plugin Meta-Skills

The `copilot-dev-team` plugin's `aspnet-tdd-xunit` is the canonical reference for the TDD workflow. This skill stays focused on PhotoGallery-specific test design (Photos, Albums, AccessCodes, Storage abstractions, Image processing); it defers to `aspnet-tdd-xunit` for general xUnit patterns. Plugin meta-skills auto-trigger by description match — prefer them on conflict.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Designing test cases for any backend feature | `aspnet-tdd-xunit` | — |
| Tests that touch EF Core / DbContext / migrations | `aspnet-tdd-xunit`, `efcore-migration-safer` | — |
| Test naming, fakes vs mocks, fixture lifetimes | `aspnet-tdd-xunit` | — |
| Validating that tests cover SOLID compliance | `solid-dry-principles` | — |
| Test scratch / probe code | — | `scratch-discipline` |

**Workflow callouts** (where each meta-skill triggers inside this skill's existing phases):

- *→ Phase 1 (Test Design) and Phase 2 (RED) — consult `aspnet-tdd-xunit` for canonical Arrange-Act-Assert, naming, and fixture patterns.*
- *→ Phase 3 (GREEN) and Phase 4 (REFACTOR) — consult `solid-dry-principles` to ensure refactors don't violate SOLID.*
- *→ Any DB-touching test — consult `aspnet-tdd-xunit` (DbTestBase patterns) and `efcore-migration-safer` (migration-aware tests).*

## Core Principle

**Tests come FIRST. Always.**

1. Write tests that define expected behavior
2. Run tests (they fail - RED phase)
3. Write minimal code to make tests pass (GREEN phase)
4. Refactor code while keeping tests passing (REFACTOR phase)
5. Tests become your regression detection system

## TDD Workflow for PhotoGallery

### Phase 1: Test Design

Before writing ANY production code:

1. **Analyze the requirement** - What should the feature do?
2. **Identify test scenarios** - Happy path, edge cases, error conditions
3. **List test cases** - Each test validates one behavior
4. **Write test class** - Name: `{FeatureName}Tests` in `PhotoGallery.Tests`
5. **Write test methods** - Follow Arrange-Act-Assert pattern

*→ consult `aspnet-tdd-xunit` for Arrange-Act-Assert patterns, test naming conventions, and fixture design.*

### Phase 2: Red Phase (Tests Fail)

```csharp
[Fact]
public void UploadPhoto_Should_Store_With_Correct_Path_Structure()
{
    // Arrange
    var albumId = Guid.NewGuid();
    var photoId = Guid.NewGuid();
    var fileName = "test.jpg";
    
    // Act
    var photo = new Photo
    {
        Id = photoId,
        AlbumId = albumId,
        FileName = fileName,
        StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg"
    };
    
    // Assert
    Assert.StartsWith("photogallery/", photo.StorageKey);
    Assert.Contains(albumId.ToString(), photo.StorageKey);
    Assert.EndsWith("original.jpg", photo.StorageKey);
}
```

- Tests fail because features don't exist yet
- This is NORMAL and EXPECTED
- Red = Baseline established

### Phase 3: Green Phase (Tests Pass)

Write minimal code to make tests pass:

```csharp
public class Photo
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string FileName { get; set; }
    public string StorageKey { get; set; }
}
```

Run tests - they pass ✓

*→ consult `solid-dry-principles` to validate that GREEN-phase code follows SOLID principles.*

### Phase 4: Refactor Phase (Keep Tests Passing)

Improve code quality while tests verify nothing breaks:

```csharp
public class Photo
{
    public Guid Id { get; private set; }
    public Guid AlbumId { get; private set; }
    public string FileName { get; private set; }
    public string StorageKey { get; private set; }
    
    public static Photo Create(Guid albumId, Guid photoId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("FileName required");
        
        return new Photo
        {
            Id = photoId,
            AlbumId = albumId,
            FileName = fileName,
            StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg"
        };
    }
}
```

Run tests - they still pass ✓

*→ consult `solid-dry-principles` to ensure refactored code maintains SOLID compliance and improves design.*

## Test Structure Pattern

All PhotoGallery tests follow this structure:

```csharp
namespace PhotoGallery.Tests;

public class {FeatureName}Tests
{
    // Test 1: Happy path / primary behavior
    [Fact]
    public void {MethodName}_Should_{ExpectedBehavior}()
    {
        // Arrange - Set up test data
        var input = CreateTestData();
        
        // Act - Perform the action
        var result = SystemUnderTest.PerformAction(input);
        
        // Assert - Verify the result
        Assert.Equal(expectedValue, result);
    }
    
    // Test 2: Edge case
    [Fact]
    public void {MethodName}_Should_{EdgeCaseBehavior}_When_{Condition}()
    {
        // Arrange
        var input = CreateEdgeCaseData();
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            SystemUnderTest.PerformAction(input)
        );
        Assert.Contains("expected error message", exception.Message);
    }
    
    // Test 3: Error condition
    [Fact]
    public void {MethodName}_Should_Throw_When_{ErrorCondition}()
    {
        // Arrange
        var badInput = CreateInvalidData();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            SystemUnderTest.PerformAction(badInput)
        );
    }
}
```

## Test Categories for PhotoGallery

### 1. Entity Tests (Domain Model)

**Purpose**: Validate business logic in entities

```csharp
[Fact]
public void Album_Create_Should_Initialize_All_Required_Fields()
{
    // Verify Album.Create() produces valid entities
}

[Fact]
public void AccessCode_Should_Detect_Expiration()
{
    // Verify expiration logic works
}
```

### 2. Repository Tests (Data Access)

**Purpose**: Validate CRUD operations and queries

*→ consult `aspnet-tdd-xunit` and `efcore-migration-safer` for DbContext mocking, in-memory database patterns, and EF Core-specific test fixtures.*

```csharp
[Fact]
public async Task GetByIdAsync_Should_Return_Album_When_Exists()
{
    // Verify repository retrieves correct data
}

[Fact]
public async Task GetByIdAsync_Should_Return_Null_When_Not_Found()
{
    // Verify repository handles missing data
}
```

### 3. Service Tests (Business Logic)

**Purpose**: Validate service behavior with mocked dependencies

```csharp
[Fact]
public async Task UploadPhotoAsync_Should_Queue_For_Processing()
{
    // Mock: IStorageProvider, IRepository, IImageProcessor
    // Test: Photo is stored, queued, and returns success response
}

[Fact]
public async Task ProcessPhotoAsync_Should_Generate_All_Versions()
{
    // Mock: IImageProcessor, IStorageProvider
    // Test: All 4 quality levels are created
}
```

### 4. API Endpoint Tests (Controllers)

**Purpose**: Validate HTTP behavior and response codes

```csharp
[Fact]
public async Task UploadPhoto_Should_Return_200_With_SuccessfulUploads()
{
    // Arrange: Create test album and mock storage
    var albumId = Guid.NewGuid();
    var mockStorage = new Mock<IStorageProvider>();
    
    // Act: Call endpoint
    var response = await controller.UploadPhotos(albumId, formFiles);
    
    // Assert: Verify response structure
    Assert.NotNull(response.SuccessfulUploads);
    Assert.Empty(response.Errors);
}

[Fact]
public async Task UploadPhoto_Should_Return_400_For_Invalid_Album()
{
    // Arrange
    var invalidAlbumId = Guid.Empty;
    
    // Act & Assert
    var response = controller.UploadPhotos(invalidAlbumId, files);
    Assert.Equal(400, response.StatusCode);
}
```

## xUnit + Moq Patterns

### Basic Fact Test

```csharp
[Fact]
public void SimpleTest()
{
    var result = Calculator.Add(2, 3);
    Assert.Equal(5, result);
}
```

### Parameterized Tests (Theory)

```csharp
[Theory]
[InlineData(2, 3, 5)]
[InlineData(0, 0, 0)]
[InlineData(-1, 1, 0)]
public void Add_Should_Return_Sum(int a, int b, int expected)
{
    var result = Calculator.Add(a, b);
    Assert.Equal(expected, result);
}
```

### Mocking Dependencies

```csharp
[Fact]
public async Task UploadPhoto_Should_Call_StorageProvider()
{
    // Arrange
    var mockStorage = new Mock<IStorageProvider>();
    mockStorage
        .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
        .ReturnsAsync("photo-key");
    
    var service = new PhotoService(mockStorage.Object);
    
    // Act
    var result = await service.UploadAsync(stream, "image/jpeg");
    
    // Assert
    Assert.Equal("photo-key", result);
    mockStorage.Verify(
        x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg"),
        Times.Once
    );
}
```

### Exception Testing

```csharp
[Fact]
public void Photo_Create_Should_Throw_When_Title_Empty()
{
    var exception = Assert.Throws<ArgumentException>(() =>
        Photo.Create("", "desc")
    );
    Assert.Contains("Title is required", exception.Message);
}

// Or with async
[Fact]
public async Task UploadAsync_Should_Throw_When_File_Too_Large()
{
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        service.UploadAsync(largeStream)
    );
}
```

## PhotoGallery-Specific Test Scenarios

### Upload Feature Tests

```csharp
public class PhotoUploadTests
{
    [Fact]
    public void StoragePath_Should_Follow_Convention()
    {
        // photogallery/<album_guid>/<photo_guid>/{quality}.jpg
    }
    
    [Fact]
    public async Task Upload_Should_Queue_For_Processing()
    {
        // Photo queued with Pending status
    }
    
    [Fact]
    public async Task Upload_Should_Return_Processing_Job_Id()
    {
        // Client receives job ID for tracking
    }
    
    [Fact]
    public async Task Upload_Should_Handle_Multiple_Files()
    {
        // Batch upload works correctly
    }
    
    [Fact]
    public async Task Upload_Should_Fail_Gracefully_On_Bad_File()
    {
        // Invalid files don't crash system
    }
}
```

### Processing Queue Tests

```csharp
public class ProcessingQueueTests
{
    [Fact]
    public void Queue_Should_Track_Status_Transitions()
    {
        // Pending -> Processing -> Complete
    }
    
    [Fact]
    public void Queue_Should_Retry_On_Error()
    {
        // RetryCount increments, ErrorMessage recorded
    }
    
    [Fact]
    public async Task ProcessingWorker_Should_Generate_All_Versions()
    {
        // High, Medium, Low, Raw versions created
    }
}
```

### Storage Provider Tests

```csharp
public class MinioStorageProviderTests
{
    [Fact]
    public async Task UploadAsync_Should_Create_Bucket_If_Missing()
    {
        // EnsureBucketExists called automatically
    }
    
    [Fact]
    public async Task UploadAsync_Should_Store_With_Correct_Path()
    {
        // Verify S3 key matches expected format
    }
    
    [Fact]
    public async Task UploadAsync_Should_Throw_On_Connection_Error()
    {
        // Connection failures handled properly
    }
}
```

## Running Tests

### Run all tests
```bash
dotnet test PhotoGallery.Tests
```

### Run specific test class
```bash
dotnet test PhotoGallery.Tests --filter "ClassName=PhotoUploadTests"
```

### Run with verbose output
```bash
dotnet test PhotoGallery.Tests -v d
```

### Run and generate coverage report
```bash
dotnet test PhotoGallery.Tests /p:CollectCoverage=true
```

## Test File Organization

```
PhotoGallery.Tests/
├── PhotoGalleryTests.cs           # Core domain tests (Album, Photo, AccessCode, ProcessingQueue)
├── PhotoUploadTests.cs             # Upload feature tests
├── StorageProviderTests.cs         # MinIO integration tests
├── ImageProcessingTests.cs         # Image processing service tests
├── AuthenticationTests.cs          # Auth middleware and endpoint tests
├── ApiEndpointTests.cs             # Controller and HTTP tests
└── IntegrationTests.cs             # Full feature flow tests
```

## Regression Prevention

Tests are your insurance policy:

1. **Before changing code** - Run all tests
2. **After changes** - Run all tests again
3. **Test fails** - Your change broke something
4. **Add new feature** - Write tests first
5. **Find a bug** - Write a test that catches it
6. **Fix the bug** - Now that test passes, preventing regression

## Best Practices

✅ **DO:**
- Write tests for all public methods
- Test happy path, edge cases, error conditions
- Use descriptive test names
- Keep tests simple and focused (one assertion per test usually)
- Mock external dependencies (storage, databases, APIs)
- Test behavior, not implementation
- Organize tests by feature/class

❌ **DON'T:**
- Write tests after implementation (defeats TDD purpose)
- Skip edge cases and error conditions
- Create brittle tests that break on refactoring
- Test implementation details instead of behavior
- Leave commented-out tests
- Write tests that depend on other tests
- Ignore failing tests

## Integration with Backend Developer Skill

**Backend developer workflow:**

1. **FIRST** - Consult this TDD skill to design test cases
2. **SECOND** - Write xUnit tests in PhotoGallery.Tests
3. **THIRD** - Run tests (RED phase)
4. **FOURTH** - Write minimal implementation to pass tests (GREEN phase)
5. **FIFTH** - Refactor while keeping tests green (REFACTOR phase)
6. **SIXTH** - Consult architect skill for SOLID/DRY validation
7. **SEVENTH** - Commit with both tests and implementation

## Example: Complete TDD Workflow

### Feature: Upload photo with new storage structure

**Step 1: Design tests (this skill)**
```
- Test storage path format: photogallery/<album>/<photo>/{quality}.jpg
- Test upload creates database record
- Test upload returns processing job ID
- Test upload handles errors gracefully
```

**Step 2: Write tests (RED)**
```csharp
// PhotoUploadTests.cs - Tests fail because Photo class doesn't exist yet
```

**Step 3: Implement code (GREEN)**
```csharp
// Photo.cs - Minimal implementation to pass tests
```

**Step 4: Refactor (REFACTOR)**
```csharp
// Photo.cs - Add validation, factory methods, while tests stay green
```

**Step 5: Validate architecture (architect skill)**
```
// Check SOLID, DRY, clean architecture compliance
```

**Result**: Feature is complete, tested, and follows architecture


## Cross-cutting plugin skills (always-on)

These copilot-dev-team meta-skills apply regardless of phase:

- `scratch-discipline` — probe/spike code in .copilot/scratch/<task-id>/, not committed test files.
- `secret-hygiene` — no secrets in test fixtures or appsettings.Test.json.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only.
- `copilot-memory-update` — record durable test-policy decisions.
