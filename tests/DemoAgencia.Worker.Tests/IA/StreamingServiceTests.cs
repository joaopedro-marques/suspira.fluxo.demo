using DemoAgencia.Worker.IA;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class StreamingServiceTests
{
    private readonly Mock<ILogger<StreamingService>> _loggerMock;
    private readonly StreamingService _streamingService;

    public StreamingServiceTests()
    {
        _loggerMock = new Mock<ILogger<StreamingService>>();
        _streamingService = new StreamingService(_loggerMock.Object);
    }

    [Fact]
    public async Task ProcessarStreamingAsync_WithSingleChunk_ShouldSendAndEdit()
    {
        var chunks = GetAsyncEnumerable(new[] { "Hello" });
        long? sentMessageId = null;
        string? sentText = null;
        string? editedText = null;

        await _streamingService.ProcessarStreamingAsync(
            123,
            chunks,
            async (text) =>
            {
                sentMessageId = 1;
                sentText = text;
                return 1;
            },
            async (messageId, text) =>
            {
                editedText = text;
            },
            CancellationToken.None);

        sentMessageId.Should().Be(1);
        sentText.Should().Be("Hello");
        editedText.Should().Be("Hello");
    }

    [Fact]
    public async Task ProcessarStreamingAsync_WithMultipleChunks_ShouldConcatenate()
    {
        var chunks = GetAsyncEnumerable(new[] { "Hello", " ", "World" });
        var sentTexts = new List<string>();
        var editedTexts = new List<string>();

        await _streamingService.ProcessarStreamingAsync(
            123,
            chunks,
            async (text) =>
            {
                sentTexts.Add(text);
                return 1;
            },
            async (messageId, text) =>
            {
                editedTexts.Add(text);
            },
            CancellationToken.None);

        sentTexts.Should().HaveCount(1);
        sentTexts[0].Should().Be("Hello");
        editedTexts.Should().Contain("Hello World");
    }

    [Fact]
    public async Task ProcessarStreamingAsync_WithEmptyStream_ShouldNotSend()
    {
        var chunks = GetAsyncEnumerable(Array.Empty<string>());
        var sendCalled = false;

        await _streamingService.ProcessarStreamingAsync(
            123,
            chunks,
            async (text) =>
            {
                sendCalled = true;
                return 1;
            },
            async (messageId, text) => { },
            CancellationToken.None);

        sendCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessarStreamingAsync_ShouldThrottleEdits()
    {
        var chunks = GetAsyncEnumerableWithDelay(
            new[] { "A", "B", "C", "D", "E" },
            TimeSpan.FromMilliseconds(100));

        var editCount = 0;

        await _streamingService.ProcessarStreamingAsync(
            123,
            chunks,
            async (text) => 1,
            async (messageId, text) =>
            {
                editCount++;
            },
            CancellationToken.None);

        editCount.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task ProcessarStreamingAsync_ShouldAlwaysEditAtEnd()
    {
        var chunks = GetAsyncEnumerable(new[] { "Chunk1", "Chunk2" });
        var lastEditedText = "";

        await _streamingService.ProcessarStreamingAsync(
            123,
            chunks,
            async (text) => 1,
            async (messageId, text) =>
            {
                lastEditedText = text;
            },
            CancellationToken.None);

        lastEditedText.Should().Be("Chunk1Chunk2");
    }

    private static async IAsyncEnumerable<string> GetAsyncEnumerable(string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<string> GetAsyncEnumerableWithDelay(
        string[] items,
        TimeSpan delay)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Delay(delay);
        }
    }
}
