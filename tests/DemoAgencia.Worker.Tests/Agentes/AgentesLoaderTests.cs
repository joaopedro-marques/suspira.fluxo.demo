using DemoAgencia.Worker.Agentes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Agentes;

public class AgentesLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<AgentesLoader>> _loggerMock;

    public AgentesLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentes-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<ILogger<AgentesLoader>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StartAsync_WithValidFiles_LoadsAllAgents()
    {
        WriteAgentFile("router", """
            ---
            modelo: deepseek/deepseek-v3.2
            temperatura: 0.2
            max_tokens: 2000
            ---

            Voce e o router.
            """);
        WriteAgentFile("redator", """
            ---
            modelo: qwen/qwen3.7-plus
            temperatura: 0.8
            max_tokens: 2000
            ---

            Voce e um redator.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.Obter("router").Nome.Should().Be("router");
        loader.Obter("redator").Nome.Should().Be("redator");
    }

    [Fact]
    public async Task StartAsync_WithValidFrontmatter_ParsesAllFields()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: 0.5
            max_tokens: 1500
            ---

            Persona do agente de teste.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.Obter("teste");
        agente.Modelo.Should().Be("openai/gpt-4o");
        agente.Temperatura.Should().Be(0.5);
        agente.MaxTokens.Should().Be(1500);
        agente.Persona.Should().Contain("Persona do agente de teste.");
    }

    [Fact]
    public async Task StartAsync_ExtractsPersonaBodyWithoutFrontmatter()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: 0.5
            max_tokens: 1000
            ---

            # Titulo

            Corpo da persona com multiplas linhas.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.Obter("teste");
        agente.Persona.Should().Contain("# Titulo");
        agente.Persona.Should().Contain("Corpo da persona");
        agente.Persona.Should().NotContain("---");
        agente.Persona.Should().NotContain("modelo:");
    }

    [Fact]
    public async Task StartAsync_WithMissingFrontmatter_ThrowsException()
    {
        File.WriteAllText(Path.Combine(_tempDir, "teste.md"), "Apenas texto sem frontmatter.");

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste.md*");
    }

    [Fact]
    public async Task StartAsync_WithMissingModelo_ThrowsException()
    {
        WriteAgentFile("teste", """
            ---
            temperatura: 0.5
            max_tokens: 1000
            ---

            Persona.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste*modelo*");
    }

    [Fact]
    public async Task StartAsync_WithInvalidTemperatura_ThrowsException()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: 5.0
            max_tokens: 1000
            ---

            Persona.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste*temperatura*");
    }

    [Fact]
    public async Task StartAsync_WithNegativeTemperatura_ThrowsException()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: -0.1
            max_tokens: 1000
            ---

            Persona.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste*temperatura*");
    }

    [Fact]
    public async Task StartAsync_WithMaxTokensZero_ThrowsException()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: 0.5
            max_tokens: 0
            ---

            Persona.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste*max_tokens*");
    }

    [Fact]
    public async Task StartAsync_WithEmptyPersonaBody_ThrowsException()
    {
        WriteAgentFile("teste", """
            ---
            modelo: openai/gpt-4o
            temperatura: 0.5
            max_tokens: 1000
            ---

            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*teste*persona*");
    }

    [Fact]
    public async Task StartAsync_WithDirectoryNotFound_ThrowsException()
    {
        var loader = new AgentesLoader(_loggerMock.Object, Path.Combine(_tempDir, "inexistente"));

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nao encontrado*");
    }

    [Fact]
    public async Task Obter_WithUnknownName_ThrowsException()
    {
        WriteAgentFile("router", """
            ---
            modelo: deepseek/deepseek-v3.2
            temperatura: 0.2
            max_tokens: 2000
            ---

            Voce e o router.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var act = () => loader.Obter("inexistente");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*inexistente*");
    }

    [Fact]
    public async Task Obter_IsCaseInsensitive()
    {
        WriteAgentFile("Router", """
            ---
            modelo: deepseek/deepseek-v3.2
            temperatura: 0.2
            max_tokens: 2000
            ---

            Router persona.
            """);

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.Obter("router").Nome.Should().Be("router");
        loader.Obter("ROUTER").Nome.Should().Be("router");
    }

    [Fact]
    public async Task StartAsync_IgnoresNonMarkdownFiles()
    {
        WriteAgentFile("router", """
            ---
            modelo: deepseek/deepseek-v3.2
            temperatura: 0.2
            max_tokens: 2000
            ---

            Router.
            """);
        File.WriteAllText(Path.Combine(_tempDir, "README.txt"), "ignored");

        var loader = new AgentesLoader(_loggerMock.Object, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.Obter("router").Should().NotBeNull();
    }

    private void WriteAgentFile(string nameSemExt, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, $"{nameSemExt}.md"), content);
    }
}
