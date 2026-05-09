using Microsoft.Extensions.Logging;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Services.Email;

namespace PhotoGallery.Tests;

public class MockEmailServiceTests
{
    private static MockEmailService CreateService(out Mock<ILogger<MockEmailService>> loggerMock)
    {
        loggerMock = new Mock<ILogger<MockEmailService>>();
        return new MockEmailService(loggerMock.Object);
    }

    private static EmailMessage SampleMessage(string to = "user@example.com", string subject = "Hello") => new()
    {
        To = to,
        Subject = subject,
        HtmlBody = "<p>Hi</p>",
        TextBody = "Hi",
    };

    [Fact]
    public async Task SendAsync_QueuesMessage_AccessibleViaSentMessages()
    {
        var service = CreateService(out _);
        var msg = SampleMessage();

        await service.SendAsync(msg);

        Assert.Single(service.SentMessages);
        Assert.Same(msg, service.SentMessages.First());
    }

    [Fact]
    public async Task SendAsync_LogsSendInformation()
    {
        var service = CreateService(out var loggerMock);

        await service.SendAsync(SampleMessage("alice@example.com", "Verify"));

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("alice@example.com") && v.ToString()!.Contains("Verify")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SentMessages_ReturnsSnapshot()
    {
        var service = CreateService(out _);
        await service.SendAsync(SampleMessage("a@example.com"));

        var snapshot = service.SentMessages;
        Assert.Single(snapshot);

        // Mutating the service after the snapshot is taken must not affect the snapshot.
        await service.SendAsync(SampleMessage("b@example.com"));

        Assert.Single(snapshot);
        Assert.Equal(2, service.SentMessages.Count);
    }

    [Fact]
    public async Task MultipleSends_AllAccumulate()
    {
        var service = CreateService(out _);

        for (var i = 0; i < 5; i++)
        {
            await service.SendAsync(SampleMessage($"user{i}@example.com", $"Subject {i}"));
        }

        Assert.Equal(5, service.SentMessages.Count);
        var addresses = service.SentMessages.Select(m => m.To).ToList();
        for (var i = 0; i < 5; i++)
        {
            Assert.Contains($"user{i}@example.com", addresses);
        }
    }

    [Fact]
    public async Task ConcurrentSends_ThreadSafe()
    {
        var service = CreateService(out _);
        const int totalSends = 200;

        var tasks = Enumerable.Range(0, totalSends)
            .Select(i => Task.Run(() => service.SendAsync(SampleMessage($"user{i}@example.com", $"Subject {i}"))))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(totalSends, service.SentMessages.Count);
        var addresses = service.SentMessages.Select(m => m.To).ToHashSet();
        for (var i = 0; i < totalSends; i++)
        {
            Assert.Contains($"user{i}@example.com", addresses);
        }
    }

    [Fact]
    public async Task SendAsync_NullMessage_Throws()
    {
        var service = CreateService(out _);
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendAsync(null!));
    }
}
