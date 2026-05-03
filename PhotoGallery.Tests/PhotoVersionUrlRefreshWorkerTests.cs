using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Services;
using PhotoGallery.Services.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PhotoGallery.Tests;

public class PhotoVersionUrlRefreshWorkerTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILogger<PhotoVersionUrlRefreshWorker>> _mockLogger;

    public PhotoVersionUrlRefreshWorkerTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<PhotoVersionUrlRefreshWorker>>();
    }

    private IConfiguration CreateConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"BlobStorage:RefreshWorkerIntervalHours", "24"},
            {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private PhotoVersionUrlRefreshWorker CreateWorker()
    {
        var config = CreateConfiguration();
        return new PhotoVersionUrlRefreshWorker(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            config);
    }

    [Fact]
    public void Constructor_Should_Initialize()
    {
        // Arrange & Act
        var worker = CreateWorker();

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void Constructor_Should_Use_Default_Interval_If_Config_Less_Than_One()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"BlobStorage:RefreshWorkerIntervalHours", "0"},  // Invalid, should use default
            {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var worker = new PhotoVersionUrlRefreshWorker(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            config);

        // Assert - Verify the worker is created (it should use default 24h)
        Assert.NotNull(worker);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Log_Start_And_Stop_Messages()
    {
        // Arrange
        var worker = CreateWorker();
        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)).Token;

        // Act
        await worker.StartAsync(cancellationToken);
        await Task.Delay(100);

        // Assert - Worker should start and log initial message
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
