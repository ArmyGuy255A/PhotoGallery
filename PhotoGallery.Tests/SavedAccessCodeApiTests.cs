using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoGallery.Controllers;
using PhotoGallery.Data;
using PhotoGallery.Models;
using System.Security.Claims;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// EPIC-02 Slice B — tests for AccountController saved-access-codes endpoints.
/// Validates: idempotency, expiration handling, security (multi-tenancy),
/// and that delete only unlinks (does not delete the AccessCode).
/// </summary>
public class SavedAccessCodeApiTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AccountController NewController(ApplicationDbContext db, string userId)
    {
        var controller = new AccountController(db, NullLogger<AccountController>.Instance);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "Test");
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }

    private static (User user, Album album, AccessCode code) Seed(
        ApplicationDbContext db,
        string userIdSuffix = "",
        DateTime? expiration = null)
    {
        var user = new User
        {
            Id = "user-" + Guid.NewGuid().ToString("N") + userIdSuffix,
            UserName = $"u{userIdSuffix}@example.com",
            Email = $"u{userIdSuffix}@example.com"
        };
        var album = new Album { Id = Guid.NewGuid(), Title = "Album " + userIdSuffix, OwnerId = user.Id };
        var code = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = "CODE" + Guid.NewGuid().ToString("N").Substring(0, 8),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = user.Id,
            ExpirationDate = expiration
        };
        db.Users.Add(user);
        db.Albums.Add(album);
        db.AccessCodes.Add(code);
        db.SaveChanges();
        return (user, album, code);
    }

    [Fact]
    public async Task Save_CreatesRow_WhenCodeValid()
    {
        using var db = NewContext();
        var (user, album, code) = Seed(db);
        var controller = NewController(db, user.Id);

        var result = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = code.Code });

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<SavedAccessCodeDto>(created.Value);
        Assert.Equal(code.Code, dto.Code);
        Assert.Equal(album.Id.ToString(), dto.AlbumId);
        Assert.Equal(album.Title, dto.AlbumTitle);

        Assert.Single(db.SavedAccessCodes);
    }

    [Fact]
    public async Task Save_IsIdempotent()
    {
        using var db = NewContext();
        var (user, _, code) = Seed(db);
        var controller = NewController(db, user.Id);

        var first = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = code.Code });
        var second = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = code.Code });

        var firstResult = Assert.IsType<ObjectResult>(first.Result);
        Assert.Equal(201, firstResult.StatusCode);
        var secondResult = Assert.IsType<OkObjectResult>(second.Result);
        Assert.Equal(200, secondResult.StatusCode);

        var firstDto = Assert.IsType<SavedAccessCodeDto>(firstResult.Value);
        var secondDto = Assert.IsType<SavedAccessCodeDto>(secondResult.Value);
        Assert.Equal(firstDto.Id, secondDto.Id);

        Assert.Single(db.SavedAccessCodes);
    }

    [Fact]
    public async Task Save_Returns404_WhenCodeInvalid()
    {
        using var db = NewContext();
        var (user, _, _) = Seed(db);
        var controller = NewController(db, user.Id);

        var result = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = "DOES-NOT-EXIST" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Empty(db.SavedAccessCodes);
    }

    [Fact]
    public async Task Save_Returns400_WhenCodeExpired()
    {
        using var db = NewContext();
        var (user, _, code) = Seed(db, expiration: DateTime.UtcNow.AddDays(-1));
        var controller = NewController(db, user.Id);

        var result = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = code.Code });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.SavedAccessCodes);
    }

    [Fact]
    public async Task List_ReturnsOnlyCurrentUsersSaved()
    {
        using var db = NewContext();
        var (userA, _, codeA) = Seed(db, "-A");
        var (userB, _, codeB) = Seed(db, "-B");

        // User A saves their code
        var controllerA = NewController(db, userA.Id);
        await controllerA.SaveAccessCode(new SaveAccessCodeRequest { Code = codeA.Code });

        // User B saves their code
        var controllerB = NewController(db, userB.Id);
        await controllerB.SaveAccessCode(new SaveAccessCodeRequest { Code = codeB.Code });

        // User A lists — should see only their own
        var listResult = await controllerA.GetSavedAccessCodes();
        var ok = Assert.IsType<OkObjectResult>(listResult.Result);
        var dtos = Assert.IsType<List<SavedAccessCodeDto>>(ok.Value);

        Assert.Single(dtos);
        Assert.Equal(codeA.Code, dtos[0].Code);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheLink_NotTheCode()
    {
        using var db = NewContext();
        var (user, _, code) = Seed(db);
        var controller = NewController(db, user.Id);

        var saveResult = await controller.SaveAccessCode(new SaveAccessCodeRequest { Code = code.Code });
        var dto = Assert.IsType<SavedAccessCodeDto>(((ObjectResult)saveResult.Result!).Value);
        var savedId = Guid.Parse(dto.Id);

        var deleteResult = await controller.DeleteSavedAccessCode(savedId);

        Assert.IsType<NoContentResult>(deleteResult);
        Assert.Empty(db.SavedAccessCodes);
        // AccessCode still exists
        Assert.NotNull(await db.AccessCodes.FindAsync(code.Id));
    }

    [Fact]
    public async Task Delete_Returns403_WhenSavedRowBelongsToAnotherUser()
    {
        using var db = NewContext();
        var (userA, _, codeA) = Seed(db, "-A");
        var (userB, _, _) = Seed(db, "-B");

        var controllerA = NewController(db, userA.Id);
        var saveResult = await controllerA.SaveAccessCode(new SaveAccessCodeRequest { Code = codeA.Code });
        var dto = Assert.IsType<SavedAccessCodeDto>(((ObjectResult)saveResult.Result!).Value);
        var savedId = Guid.Parse(dto.Id);

        // User B tries to delete user A's saved row
        var controllerB = NewController(db, userB.Id);
        var deleteResult = await controllerB.DeleteSavedAccessCode(savedId);

        Assert.IsType<ForbidResult>(deleteResult);
        Assert.Single(db.SavedAccessCodes);
    }
}
