using Microsoft.AspNetCore.Identity;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Services;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Validates the bare-name contract: the resolver always strips the leading
/// "© " from <see cref="WatermarkService.FormatDisplayName"/> so callers can
/// safely interpolate the result into prose like "Created by {name}".
/// </summary>
public class UserDisplayNameResolverTests
{
    private static UserManager<User> MockUserManager(User? returned)
    {
        var store = new Mock<IUserStore<User>>();
        var mgr = new Mock<UserManager<User>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(returned);
        return mgr.Object;
    }

    [Fact]
    public async Task ResolveAsync_NullId_ReturnsDefault()
    {
        var resolver = new UserDisplayNameResolver(MockUserManager(null));
        var name = await resolver.ResolveAsync(null);
        Assert.Equal("Photo Gallery", name);
    }

    [Fact]
    public async Task ResolveAsync_FullName_StripsCopyrightPrefix()
    {
        var user = new User { Id = "u1", FirstName = "Phillip", LastName = "Dieppa" };
        var resolver = new UserDisplayNameResolver(MockUserManager(user));
        var name = await resolver.ResolveAsync("u1");
        Assert.Equal("Phillip Dieppa", name);
        Assert.DoesNotContain("©", name);
    }

    [Fact]
    public async Task ResolveManyAsync_DeduplicatesRepeatedIds()
    {
        var user = new User { Id = "u1", FirstName = "P", LastName = "D" };
        var store = new Mock<IUserStore<User>>();
        var mgr = new Mock<UserManager<User>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);

        var resolver = new UserDisplayNameResolver(mgr.Object);
        var map = await resolver.ResolveManyAsync(new[] { "u1", "u1", "u1", null, "" });

        Assert.Single(map);
        Assert.Equal("P D", map["u1"]);
        mgr.Verify(m => m.FindByIdAsync("u1"), Times.Once);
    }
}
