using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Observabilidade;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class RoteadorServiceTests
{
    private readonly Mock<ILogger<RoteadorService>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly RoteadorService _roteador;

    public RoteadorServiceTests()
    {
        _loggerMock = new Mock<ILogger<RoteadorService>>();
        
        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());

        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object);

        _openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object);

        _agenteLoaderMock = new Mock<AgenteLoader>(
            Mock.Of<ILogger<AgenteLoader>>(),
            Mock.Of<IConfiguration>(),
            (string?)null);

        _roteador = new RoteadorService(
            _loggerMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object);
    }

    [Fact]
    public async Task RoteearAsync_WithValidCommand_ShouldReturnAgentModel()
    {
        var agente = new AgenteDefinicao
        {
            Nome = "Dev",
            ModeloAlvo = "anthropic/claude-3.5-sonnet",
            Persona = "Voce e um dev",
            Comandos = new List<string> { "/dev" }
        };

        _agenteLoaderMock
            .Setup(x => x.ObterPorComando("/dev"))
            .Returns(agente);

        var (modelo, persona) = await _roteador.RoteearAsync(
            123,
            "mensagem",
            "/dev",
            CancellationToken.None);

        modelo.Should().Be("anthropic/claude-3.5-sonnet");
        persona.Should().Be("Voce e um dev");
    }

    [Fact]
    public async Task RoteearAsync_WithInvalidCommand_ShouldUseClassifier()
    {
        _agenteLoaderMock
            .Setup(x => x.ObterPorComando("/invalid"))
            .Returns((AgenteDefinicao?)null);

        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("codigo");

        var devAgent = new AgenteDefinicao
        {
            Nome = "Dev",
            Persona = "Persona dev"
        };

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao> { devAgent }.AsReadOnly());

        var (modelo, persona) = await _roteador.RoteearAsync(
            123,
            "mensagem",
            "/invalid",
            CancellationToken.None);

        modelo.Should().Be("anthropic/claude-3.5-sonnet");
        persona.Should().Be("Persona dev");
    }

    [Fact]
    public async Task RoteearAsync_WithoutCommand_ShouldUseClassifier()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync("como fazer um loop", It.IsAny<CancellationToken>()))
            .ReturnsAsync("codigo");

        var devAgent = new AgenteDefinicao
        {
            Nome = "Dev",
            Persona = "Persona dev"
        };

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao> { devAgent }.AsReadOnly());

        var (modelo, persona) = await _roteador.RoteearAsync(
            123,
            "como fazer um loop",
            null,
            CancellationToken.None);

        modelo.Should().Be("anthropic/claude-3.5-sonnet");
        persona.Should().Be("Persona dev");
    }

    [Fact]
    public async Task RoteearAsync_WithCodigoCategory_ShouldReturnClaude()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("codigo");

        var devAgent = new AgenteDefinicao
        {
            Nome = "Dev",
            Persona = "Dev persona"
        };

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao> { devAgent }.AsReadOnly());

        var (modelo, _) = await _roteador.RoteearAsync(123, "mensagem", null, CancellationToken.None);

        modelo.Should().Be("anthropic/claude-3.5-sonnet");
    }

    [Fact]
    public async Task RoteearAsync_WithEstrategiaCategory_ShouldReturnLlama()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("estrategia");

        var estrategistaAgent = new AgenteDefinicao
        {
            Nome = "Estrategista",
            Persona = "Estrategista persona"
        };

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao> { estrategistaAgent }.AsReadOnly());

        var (modelo, _) = await _roteador.RoteearAsync(123, "mensagem", null, CancellationToken.None);

        modelo.Should().Be("meta-llama/llama-3.1-70b-instruct");
    }

    [Fact]
    public async Task RoteearAsync_WithCopyCategory_ShouldReturnClaude()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("copy");

        var redatorAgent = new AgenteDefinicao
        {
            Nome = "Redator",
            Persona = "Redator persona"
        };

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao> { redatorAgent }.AsReadOnly());

        var (modelo, _) = await _roteador.RoteearAsync(123, "mensagem", null, CancellationToken.None);

        modelo.Should().Be("anthropic/claude-3.5-sonnet");
    }

    [Fact]
    public async Task RoteearAsync_WithGeralCategory_ShouldReturnGeminiFlash()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("geral");

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        var (modelo, _) = await _roteador.RoteearAsync(123, "mensagem", null, CancellationToken.None);

        modelo.Should().Be("google/gemini-flash-1.5");
    }

    [Fact]
    public async Task RoteearAsync_WithUnknownCategory_ShouldReturnGeminiFlash()
    {
        _openRouterMock
            .Setup(x => x.ClassificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("desconhecido");

        _agenteLoaderMock
            .Setup(x => x.ListarAgentes())
            .Returns(new List<AgenteDefinicao>().AsReadOnly());

        var (modelo, _) = await _roteador.RoteearAsync(123, "mensagem", null, CancellationToken.None);

        modelo.Should().Be("google/gemini-flash-1.5");
    }
}
