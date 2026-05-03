# PhotoGallery Test-Driven Development (TDD) Workflow

## Overview

PhotoGallery uses **Test-Driven Development (TDD)** as the mandatory process for building all backend features, services, and API endpoints. Tests are written FIRST, before implementation, and serve as:

1. **Specification** - Tests define exactly what code should do
2. **Regression Detection** - Tests catch breaking changes immediately
3. **Documentation** - Tests show how to use the code
4. **Design Guide** - Tests ensure code is testable and loosely coupled

## The TDD Workflow (Red-Green-Refactor)

### Phase 1: RED - Write Failing Tests

**Action**: Write test cases that describe the feature you want to build

```csharp
[Fact]
public void Photo_Should_Store_With_Correct_Path_Structure()
{
    // Arrange
    var albumId = Guid.NewGuid();
    var photoId = Guid.NewGuid();
    
    // Act
    var photo = new Photo
    {
        Id = photoId,
        AlbumId = albumId,
        StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg"
    };
    
    // Assert - This will fail if the class doesn't exist yet!
    Assert.StartsWith("photogallery/", photo.StorageKey);
}
```

**Result**: Tests fail because the feature doesn't exist yet. This is EXPECTED and GOOD.

```
FAILED: Photo_Should_Store_With_Correct_Path_Structure
Error: The type or namespace name 'Photo' could not be found
```

### Phase 2: GREEN - Write Minimal Implementation

**Action**: Write MINIMAL code to make the tests pass

```csharp
public class Photo
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string StorageKey { get; set; }
}
```

**Result**: Tests pass! ✓

```
PASSED: Photo_Should_Store_With_Correct_Path_Structure
```

### Phase 3: REFACTOR - Improve Code Quality

**Action**: Add validation, encapsulation, factory methods - while keeping tests passing

```csharp
public class Photo
{
    public Guid Id { get; private set; }
    public Guid AlbumId { get; private set; }
    public string StorageKey { get; private set; }
    
    private Photo() { }
    
    public static Photo Create(Guid albumId, Guid photoId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("FileName is required");
        
        return new Photo
        {
            Id = photoId,
            AlbumId = albumId,
            StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg"
        };
    }
}
```

**Result**: Tests still pass! ✓ Code is now better designed.

## How to Get Started (Step-by-Step)

### Step 1: Understand the Requirement

Example: "I want to upload photos and store them in MinIO with a specific path structure"

### Step 2: Consult the TDD Skill

Navigate to: `skills/tdd-unit-testing-skill/SKILL.md`

This guide covers:
- Test patterns for xUnit
- How to use Moq for mocking
- How to structure tests
- Examples specific to PhotoGallery

### Step 3: Design Your Test Cases

Before writing a single line of code, list out what should happen:

**Test Cases for Photo Upload:**
- ✅ Photo storage path follows format: `photogallery/<album>/<photo>/{quality}.jpg`
- ✅ Upload creates database record
- ✅ Upload returns processing job ID
- ✅ Upload handles multiple files
- ✅ Upload fails gracefully on bad file
- ✅ Thumbnail version is created
- ✅ Photo marked for processing

### Step 4: Write the Test Class

Location: `PhotoGallery.Tests/PhotoUploadTests.cs`

```csharp
namespace PhotoGallery.Tests;

public class PhotoUploadTests
{
    [Fact]
    public void Photo_StoragePath_Should_Include_AlbumAndPhotoIds()
    {
        // Arrange
        var albumId = Guid.Parse("c373c777-c47a-4648-937e-c92006319c30");
        var photoId = Guid.NewGuid();
        
        // Act
        var storagePath = $"photogallery/{albumId}/{photoId}/original.jpg";
        
        // Assert
        Assert.Contains(albumId.ToString(), storagePath);
        Assert.Contains(photoId.ToString(), storagePath);
        Assert.StartsWith("photogallery/", storagePath);
        Assert.EndsWith("/original.jpg", storagePath);
    }
    
    [Fact]
    public async Task Upload_Should_Return_Processing_JobId()
    {
        // Write your test here
    }
}
```

### Step 5: Run Tests (Watch Them Fail!)

```bash
cd PhotoGallery
dotnet test PhotoGallery.Tests --filter "ClassName=PhotoUploadTests"
```

You should see failures. This is the RED phase - completely normal and expected.

### Step 6: Write Implementation

Create the minimum code needed to pass the tests:

Location: `PhotoGallery/Models/Photo.cs` (for models)
Location: `PhotoGallery/Services/PhotoUploadService.cs` (for services)

```csharp
public class Photo
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string FileName { get; set; }
    public string StorageKey { get; set; }
}
```

### Step 7: Run Tests Again (Watch Them Pass!)

```bash
dotnet test PhotoGallery.Tests
```

All tests should pass now. This is the GREEN phase.

### Step 8: Refactor and Improve

While tests are passing, improve code quality:
- Add validation
- Add private setters
- Add factory methods
- Extract constants
- Apply design patterns

**Verify tests still pass after each refactoring!**

### Step 9: Consult Architect Skill

Review: `skills/photogallery-architect-skill/SKILL.md`

Ensure your code follows:
- ✅ SOLID principles
- ✅ DRY (don't repeat yourself)
- ✅ Clean architecture layering
- ✅ PhotoGallery patterns

### Step 10: Commit

Commit tests and implementation together:

```bash
git add PhotoGallery/Models/Photo.cs
git add PhotoGallery.Tests/PhotoUploadTests.cs
git commit -m "feat: Add Photo model with storage path validation

- Photo stores path as photogallery/<album>/<photo>/original.jpg
- Factory method Photo.Create() validates inputs
- 3 tests validate storage path structure

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Test Organization

### By Feature (Recommended)

```
PhotoGallery.Tests/
├── PhotoUploadTests.cs              # Tests for photo upload feature
├── ImageProcessingTests.cs          # Tests for image processing
├── StorageProviderTests.cs          # Tests for MinIO integration
├── AuthenticationTests.cs           # Tests for auth middleware
├── AccessCodeTests.cs               # Tests for access codes
└── PhotoGalleryTests.cs             # Core domain model tests
```

### Running Specific Tests

```bash
# Run all tests
dotnet test PhotoGallery.Tests

# Run specific test class
dotnet test PhotoGallery.Tests --filter "ClassName=PhotoUploadTests"

# Run specific test method
dotnet test PhotoGallery.Tests --filter "Name~Should_Store_With_Correct_Path"

# Run with verbose output
dotnet test PhotoGallery.Tests -v detailed
```

## Test Examples

### Example 1: Entity Validation

```csharp
[Fact]
public void Album_Create_Should_Require_Title()
{
    // Arrange & Act & Assert
    var exception = Assert.Throws<ArgumentException>(() =>
        Album.Create(string.Empty, "desc", "userId", "creator")
    );
    Assert.Contains("Title is required", exception.Message);
}
```

### Example 2: Service with Mocked Dependencies

```csharp
[Fact]
public async Task UploadPhoto_Should_Call_StorageProvider()
{
    // Arrange
    var mockStorage = new Mock<IStorageProvider>();
    mockStorage
        .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
        .ReturnsAsync("photo-key");
    
    var mockRepo = new Mock<IRepository<Photo>>();
    var service = new PhotoUploadService(mockStorage.Object, mockRepo.Object);
    
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

### Example 3: Parameterized Tests

```csharp
[Theory]
[InlineData("high", 50)]
[InlineData("medium", 75)]
[InlineData("low", 85)]
[InlineData("raw", 100)]
public void CompressionProfile_Should_Have_Correct_Quality(string name, int expectedQuality)
{
    var profile = new CompressionProfile { Name = name, QualityPercentage = expectedQuality };
    Assert.Equal(expectedQuality, profile.QualityPercentage);
}
```

## Common Patterns

### Testing Async Methods

```csharp
[Fact]
public async Task ProcessPhotoAsync_Should_Complete_Successfully()
{
    // Act
    await service.ProcessPhotoAsync(photoId);
    
    // Assert - verify side effects
    var processedPhoto = await repo.GetByIdAsync(photoId);
    Assert.NotNull(processedPhoto.ProcessedDate);
}
```

### Testing Exception Handling

```csharp
[Fact]
public async Task UploadAsync_Should_Throw_When_File_Too_Large()
{
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        service.UploadAsync(largeStream, "image/jpeg")
    );
    Assert.Contains("File size exceeds", exception.Message);
}
```

### Testing with Multiple Scenarios

```csharp
[Theory]
[InlineData(0, false)]      // Empty album
[InlineData(1, true)]       // Single photo
[InlineData(100, true)]     // Many photos
public void Album_HasPhotos_Should_Reflect_PhotoCount(int photoCount, bool expectedHasPhotos)
{
    // Arrange
    var album = new Album { /* setup */ };
    for (int i = 0; i < photoCount; i++)
    {
        album.Photos.Add(new Photo { /* setup */ });
    }
    
    // Act & Assert
    Assert.Equal(expectedHasPhotos, album.HasPhotos());
}
```

## Regression Prevention

Tests catch bugs EARLY:

**Scenario**: You change the storage path format

```csharp
// OLD CODE
StorageKey = $"photos/{albumId}/{photoId}/{quality}.jpg";

// CHANGED TO
StorageKey = $"gallery/{albumId}/{photoId}/{quality}.jpg";  // Oops!
```

**Result**: Tests fail immediately!

```
FAILED: Photo_StoragePath_Should_Follow_Convention
Assert.StartsWith("photogallery/", storageKey) failed
Actual: "gallery/..."
```

You catch the mistake BEFORE pushing to production.

## Best Practices

✅ **DO:**
- Write tests BEFORE implementation
- Test happy path AND edge cases
- Use descriptive test names
- Mock external dependencies (storage, databases, APIs)
- Keep tests simple and focused
- Run tests frequently (after every change)
- Test behavior, not implementation

❌ **DON'T:**
- Write tests after implementation
- Skip edge cases and error conditions
- Test implementation details
- Leave commented-out tests
- Write tests that depend on other tests
- Ignore failing tests
- Commit code without running tests

## Workflow Integration

### How TDD Integrates with Skills

1. **Developer** - Starts implementing a feature
   ↓
2. **Consults TDD Skill** - Plans test cases using `skills/tdd-unit-testing-skill/SKILL.md`
   ↓
3. **Writes Tests** - Creates failing tests in `PhotoGallery.Tests/`
   ↓
4. **Writes Implementation** - Minimal code to pass tests
   ↓
5. **Refactors** - Improves code while tests stay green
   ↓
6. **Consults Architect Skill** - Validates SOLID/DRY with `skills/photogallery-architect-skill/SKILL.md`
   ↓
7. **Commits** - Tests and implementation together

## Quick Reference

| Task | Command |
|------|---------|
| Run all tests | `dotnet test PhotoGallery.Tests` |
| Run specific test class | `dotnet test PhotoGallery.Tests --filter "ClassName=PhotoUploadTests"` |
| Run with coverage | `dotnet test PhotoGallery.Tests /p:CollectCoverage=true` |
| Watch for changes | `dotnet watch test` |
| View test output | `dotnet test -v d` |

## Questions?

- **How do I test X?** → Check `skills/tdd-unit-testing-skill/SKILL.md`
- **Is my test structure right?** → Compare with examples in `PhotoGallery.Tests/`
- **Is my code architecture right?** → Consult `skills/photogallery-architect-skill/SKILL.md`
- **Do I have enough tests?** → At minimum: happy path + 2 edge cases + 1 error case

## Remember

> "Make the tests pass, not the deadline."

Tests are insurance. They cost time upfront but save exponentially more time later by catching bugs before they reach production.

**Welcome to TDD at PhotoGallery! 🎉**
