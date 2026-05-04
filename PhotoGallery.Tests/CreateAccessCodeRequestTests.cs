using Xunit;
using PhotoGallery.Controllers;

namespace PhotoGallery.Tests;

/// <summary>
/// Unit tests for CreateAccessCodeRequest DTO with explicit ExpirationDate support
/// </summary>
public class CreateAccessCodeRequestTests
{
    [Fact]
    public void DTO_Should_Default_To_30_Days()
    {
        var request = new CreateAccessCodeRequest();

        Assert.False(request.ExpiresForever);
        Assert.Null(request.ExpirationDate);
        Assert.Equal(30, request.ExpirationDays);
    }

    [Fact]
    public void DTO_Should_Accept_Explicit_ExpirationDate()
    {
        var futureDate = DateTime.UtcNow.AddDays(60);
        var request = new CreateAccessCodeRequest
        {
            ExpirationDate = futureDate
        };

        Assert.Equal(futureDate, request.ExpirationDate);
        Assert.False(request.ExpiresForever);
    }

    [Fact]
    public void DTO_Should_Support_ExpiresForever_Flag()
    {
        var request = new CreateAccessCodeRequest
        {
            ExpiresForever = true
        };

        Assert.True(request.ExpiresForever);
    }

    [Fact]
    public void DTO_Should_Allow_All_Fields_Combined()
    {
        // ExpiresForever takes precedence in controller, but DTO permits all set
        var request = new CreateAccessCodeRequest
        {
            ExpiresForever = false,
            ExpirationDate = DateTime.UtcNow.AddDays(7),
            ExpirationDays = 14
        };

        Assert.False(request.ExpiresForever);
        Assert.NotNull(request.ExpirationDate);
        Assert.Equal(14, request.ExpirationDays);
    }
}
