using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.Contracts;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Observabilidade;

public class LangfuseInterceptorTests
{
    private readonly Mock<ILogger<LangfuseInterceptor>> _loggerMock;
    private readonly Mock<LangfuseClient> _langfuseClientMock;
    private readonly AnonimizadorService _anonimizador;
    private readonly LangfuseInterceptor _interceptor;

    public LangfuseInterceptorTests()
    {
        _loggerMock = new Mock<ILogger<LangfuseInterceptor>>();
        _langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        _anonimizador = new AnonimizadorService(
            TestOptions.Create(new SegurancaOptions { AnonimizarDados = false }));
        _interceptor = new LangfuseInterceptor(_loggerMock.Object, _langfuseClientMock.Object, _anonimizador);
    }

    [Fact]
    public void IniciarTrace_ShouldCreateContextWithCorrectValues()
    {
        var context = _interceptor.IniciarTrace(123, "chat-completion", "openai/gpt-4");

        context.ChatId.Should().Be(123);
        context.Operacao.Should().Be("chat-completion");
        context.Modelo.Should().Be("openai/gpt-4");
        context.StartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FinalizarTraceAsync_ShouldSetEndTime()
    {
        var context = _interceptor.IniciarTrace(123, "chat-completion", "openai/gpt-4");

        await Task.Delay(100);

        await _interceptor.FinalizarTraceAsync(
            context,
            "input",
            "output",
            CancellationToken.None);

        context.EndTime.Should().BeAfter(context.StartTime);
    }

    [Fact]
    public async Task FinalizarTraceAsync_ShouldSendTraceToLangfuse()
    {
        var context = _interceptor.IniciarTrace(123, "chat-completion", "openai/gpt-4");

        await _interceptor.FinalizarTraceAsync(
            context,
            "input message",
            "output response",
            CancellationToken.None);

        _langfuseClientMock.Verify(
            x => x.EnviarTraceAsync(
                It.Is<LangfuseTrace>(t =>
                    t.UserId == "123" &&
                    t.Model == "openai/gpt-4" &&
                    t.Name == "chat-completion"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinalizarTraceAsync_ShouldIncludeMetadata()
    {
        var context = _interceptor.IniciarTrace(456, "image-analysis", "google/gemini-flash-1.5");

        await _interceptor.FinalizarTraceAsync(
            context,
            "image",
            "description",
            CancellationToken.None);

        _langfuseClientMock.Verify(
            x => x.EnviarTraceAsync(
                It.Is<LangfuseTrace>(t =>
                    t.Metadata != null &&
                    t.Metadata.ContainsKey("chatId") &&
                    t.Metadata.ContainsKey("duration_ms")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinalizarTraceAsync_ShouldIncludeTags()
    {
        var context = _interceptor.IniciarTrace(123, "chat-completion", "openai/gpt-4");

        await _interceptor.FinalizarTraceAsync(
            context,
            "input",
            "output",
            CancellationToken.None);

        _langfuseClientMock.Verify(
            x => x.EnviarTraceAsync(
                It.Is<LangfuseTrace>(t =>
                    t.Tags != null &&
                    t.Tags.Contains("telegram") &&
                    t.Tags.Contains("chat-completion")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinalizarTraceAsync_ShouldCalculateDuration()
    {
        var context = _interceptor.IniciarTrace(123, "chat-completion", "openai/gpt-4");

        await Task.Delay(50);

        await _interceptor.FinalizarTraceAsync(
            context,
            "input",
            "output",
            CancellationToken.None);

        var duration = (context.EndTime - context.StartTime).TotalMilliseconds;
        duration.Should().BeGreaterThanOrEqualTo(50);
    }
}
