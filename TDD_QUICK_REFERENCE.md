# TDD Quick Reference Card

## The Three Phases

### 🔴 RED: Write Failing Tests
**Goal**: Specify what you want to build
```bash
dotnet test PhotoGallery.Tests
# Tests FAIL - This is expected and good!
```

### 🟢 GREEN: Write Minimal Code
**Goal**: Make tests pass (don't worry about perfection yet)
```bash
dotnet test PhotoGallery.Tests
# Tests PASS ✓
```

### 🔵 REFACTOR: Improve Quality
**Goal**: Make code better while keeping tests green
```bash
dotnet test PhotoGallery.Tests  # After each change
# Tests still PASS ✓
```

---

## Before You Code

```
Requirement → TDD Skill → Design Tests → Write Tests (RED)
                         ↓
                    Implement (GREEN)
                         ↓
                      Refactor (BLUE)
                         ↓
                 Architect Review
                         ↓
                       Commit
```

---

## File Locations

| What | Where |
|------|-------|
| **Write tests here** | `PhotoGallery.Tests/` |
| **Write code here** | `PhotoGallery/` |
| **Test examples** | `PhotoGallery.Tests/PhotoGalleryTests.cs` |
| **Mocking help** | `skills/tdd-unit-testing-skill/SKILL.md` |
| **Architecture** | `skills/photogallery-architect-skill/SKILL.md` |

---

## Common Commands

```bash
# Run all tests
dotnet test PhotoGallery.Tests

# Run specific test class
dotnet test PhotoGallery.Tests --filter "ClassName=PhotoUploadTests"

# Run specific test
dotnet test PhotoGallery.Tests --filter "Name~StoragePath"

# Watch for changes and re-run
dotnet watch test

# Verbose output
dotnet test PhotoGallery.Tests -v d

# Fail fast (stop on first failure)
dotnet test PhotoGallery.Tests --no-build --no-restore
```

---

## Test Structure (xUnit)

### Simple Test
```csharp
[Fact]
public void Something_Should_HappenCorrectly()
{
    // Arrange: Set up test data
    var albumId = Guid.NewGuid();
    
    // Act: Do the thing
    var photo = Photo.Create(albumId, "test.jpg");
    
    // Assert: Verify result
    Assert.NotNull(photo);
    Assert.Equal(albumId, photo.AlbumId);
}
```

### Parameterized Test (multiple scenarios)
```csharp
[Theory]
[InlineData("high", 50)]
[InlineData("medium", 75)]
[InlineData("low", 85)]
public void Quality_Should_Have_CorrectPercentage(string name, int expectedQuality)
{
    var profile = new CompressionProfile { Name = name, QualityPercentage = expectedQuality };
    Assert.Equal(expectedQuality, profile.QualityPercentage);
}
```

### Async Test
```csharp
[Fact]
public async Task UploadPhoto_Should_Create_DatabaseRecord()
{
    // Act
    var result = await service.UploadAsync(stream, "image/jpeg");
    
    // Assert
    Assert.NotNull(result);
}
```

### With Mocking
```csharp
[Fact]
public async Task Upload_Should_Call_StorageProvider()
{
    // Arrange
    var mockStorage = new Mock<IStorageProvider>();
    mockStorage
        .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
        .ReturnsAsync("key");
    
    var service = new PhotoUploadService(mockStorage.Object);
    
    // Act
    await service.UploadAsync(stream, "image/jpeg");
    
    // Assert
    mockStorage.Verify(x => x.UploadAsync(
        It.IsAny<string>(),
        It.IsAny<Stream>(),
        "image/jpeg"
    ), Times.Once);
}
```

---

## Naming Convention

**Test names describe WHAT should happen:**

✅ **Good:**
- `Photo_Should_Store_With_Correct_Path`
- `Upload_Should_Return_ProcessingJobId`
- `Album_Create_Should_Require_Title`
- `AccessCode_Should_Expire_After_30_Days`

❌ **Bad:**
- `Test1`
- `PhotoTest`
- `Upload`
- `DoUpload`

---

## Assertions (What to Check)

```csharp
// Equality
Assert.Equal(expected, actual);
Assert.NotEqual(expected, actual);

// Null checks
Assert.Null(value);
Assert.NotNull(value);

// Boolean
Assert.True(condition);
Assert.False(condition);

// Strings
Assert.StartsWith("prefix", value);
Assert.EndsWith("suffix", value);
Assert.Contains("substring", value);

// Collections
Assert.Empty(collection);
Assert.NotEmpty(collection);
Assert.Contains(item, collection);
Assert.Single(collection);  // Only 1 item

// Exceptions
Assert.Throws<ArgumentException>(() => DoSomething());
await Assert.ThrowsAsync<InvalidOperationException>(() => DoSomethingAsync());

// Ranges
Assert.InRange(value, min, max);
Assert.NotInRange(value, min, max);
```

---

## Mocking Basics (Moq)

```csharp
// Create a mock
var mockStorage = new Mock<IStorageProvider>();

// Setup what it should return
mockStorage
    .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
    .ReturnsAsync("key");

// Use in your code
var service = new PhotoService(mockStorage.Object);

// Verify it was called
mockStorage.Verify(
    x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg"),
    Times.Once
);

// Verify it was called specific number of times
mockStorage.Verify(x => x.UploadAsync(...), Times.Exactly(3));
mockStorage.Verify(x => x.UploadAsync(...), Times.AtLeastOnce);
mockStorage.Verify(x => x.UploadAsync(...), Times.Never);
```

---

## What to Test (Coverage Areas)

### Happy Path (Does it work?)
```csharp
[Fact]
public void Upload_Should_Store_Photo_Successfully()
{
    // Normal case, everything works
}
```

### Edge Cases (Boundary conditions?)
```csharp
[Theory]
[InlineData("")] // Empty
[InlineData(" ")] // Whitespace
[InlineData(null)] // Null
public void Create_Should_Reject_Invalid_Title(string invalidTitle)
{
    Assert.Throws<ArgumentException>(() => Album.Create(invalidTitle));
}
```

### Error Conditions (Fails gracefully?)
```csharp
[Fact]
public async Task Upload_Should_Throw_When_Storage_Unavailable()
{
    var mockStorage = new Mock<IStorageProvider>();
    mockStorage
        .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
        .ThrowsAsync(new Exception("Storage offline"));
    
    var service = new PhotoUploadService(mockStorage.Object);
    
    var exception = await Assert.ThrowsAsync<Exception>(
        () => service.UploadAsync(stream, "image/jpeg")
    );
    Assert.Contains("Storage offline", exception.Message);
}
```

---

## Git Workflow

```bash
# Create feature branch
git checkout -b feature/upload-photos

# Write tests (RED)
# Add test file and make commit
git add PhotoGallery.Tests/PhotoUploadTests.cs
git commit -m "test: Add photo upload test suite (RED - tests failing)"

# Write implementation (GREEN)
# Add implementation code and make commit
git add PhotoGallery/Services/PhotoUploadService.cs
git commit -m "feat: Add photo upload service (GREEN - tests passing)"

# Refactor (BLUE)
# Improve code, commit after tests still pass
git add PhotoGallery/Services/PhotoUploadService.cs
git commit -m "refactor: Extract validation to separate method"

# Push all commits together
git push origin feature/upload-photos

# Create PR with all three commits showing the full TDD workflow
```

---

## Quick Checklist

Before committing code:

- [ ] Read `TDD_WORKFLOW.md`
- [ ] Consult `skills/tdd-unit-testing-skill/SKILL.md`
- [ ] Write failing tests (RED)
- [ ] Write implementation (GREEN)
- [ ] Refactor (BLUE)
- [ ] Run all tests: `dotnet test PhotoGallery.Tests`
- [ ] Consult architect skill
- [ ] Commit tests + implementation together
- [ ] PR includes at least 3 commits (RED, GREEN, REFACTOR)

---

## Emergency Contact

- **TDD Questions?** → See `TDD_WORKFLOW.md`
- **Test Patterns?** → See `skills/tdd-unit-testing-skill/SKILL.md`
- **Architecture?** → See `skills/photogallery-architect-skill/SKILL.md`
- **Examples?** → See `PhotoGallery.Tests/`

---

## Remember

> Tests are the **specification** of what your code should do.
> 
> Without tests, you're just hoping your code works.
> 
> With tests, you **know** it works.

🎉 Welcome to TDD!
