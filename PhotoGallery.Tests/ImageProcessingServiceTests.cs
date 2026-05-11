using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Services.Processing;
using PhotoGallery.Interfaces;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace PhotoGallery.Tests;

public class ImageProcessingServiceTests
{
    [Fact]
    public void GetCompressionProfiles_Should_Return_Four_Profiles()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockStorage = new Mock<IStorageProvider>();
        var mockLogger = new Mock<ILogger<ImageProcessingService>>();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var service = new ImageProcessingService(mockServiceProvider.Object, mockStorage.Object, mockLogger.Object, configuration);

        // Act
        var profiles = service.GetCompressionProfiles();

        // Assert
        Assert.NotNull(profiles);
        var profileList = profiles.ToList();
        Assert.Equal(4, profileList.Count);
        Assert.Contains(profileList, p => p.Name == "thumbnail");
        Assert.Contains(profileList, p => p.Name == "low");
        Assert.Contains(profileList, p => p.Name == "medium");
        Assert.Contains(profileList, p => p.Name == "high");
    }
}
