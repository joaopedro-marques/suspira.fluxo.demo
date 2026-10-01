using DemoAgencia.Worker.Agentes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Agentes;

public class AgenteLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<AgenteLoader>> _loggerMock;
    private readonly IConfiguration _configuration;

    public AgenteLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<ILogger<AgenteLoader>>();
        _configuration = new ConfigurationBuilder().Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task StartAsync_WithValidMarkdownFile_ShouldLoadAgent()
    {
        var markdownContent = @"---
nome: TestAgent
descricao: Agente de teste
modelo_alvo: openai/gpt-4
comandos:
  - /test
---

# TestAgent

Persona de teste";

        var filePath = Path.Combine(_tempDir, "test.md");
        await File.WriteAllTextAsync(filePath, markdownContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agentes = loader.ListarAgentes();
        agentes.Should().HaveCount(1);
        agentes.First().Nome.Should().Be("TestAgent");
        agentes.First().Descricao.Should().Be("Agente de teste");
        agentes.First().ModeloAlvo.Should().Be("openai/gpt-4");
        agentes.First().Comandos.Should().Contain("/test");
    }

    [Fact]
    public async Task StartAsync_WithMultipleCommands_ShouldParseAll()
    {
        var markdownContent = @"---
nome: MultiCommand
descricao: Agente com multiplos comandos
modelo_alvo: openai/gpt-4
comandos:
  - /cmd1
  - /cmd2
  - /cmd3
---

# MultiCommand

Persona";

        var filePath = Path.Combine(_tempDir, "multi.md");
        await File.WriteAllTextAsync(filePath, markdownContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.ObterPorComando("/cmd1");
        agente.Should().NotBeNull();
        agente!.Nome.Should().Be("MultiCommand");

        agente = loader.ObterPorComando("/cmd2");
        agente.Should().NotBeNull();

        agente = loader.ObterPorComando("/cmd3");
        agente.Should().NotBeNull();
    }

    [Fact]
    public async Task ObterPorComando_WithInvalidCommand_ShouldReturnNull()
    {
        var markdownContent = @"---
nome: TestAgent
descricao: Test
modelo_alvo: openai/gpt-4
comandos:
  - /valid
---

# TestAgent

Persona";

        var filePath = Path.Combine(_tempDir, "test.md");
        await File.WriteAllTextAsync(filePath, markdownContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.ObterPorComando("/invalid");
        agente.Should().BeNull();
    }

    [Fact]
    public async Task ObterPorComando_WithCaseInsensitive_ShouldMatch()
    {
        var markdownContent = @"---
nome: TestAgent
descricao: Test
modelo_alvo: openai/gpt-4
comandos:
  - /TestCommand
---

# TestAgent

Persona";

        var filePath = Path.Combine(_tempDir, "test.md");
        await File.WriteAllTextAsync(filePath, markdownContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.ObterPorComando("/TESTCOMMAND");
        agente.Should().NotBeNull();
        agente!.Nome.Should().Be("TestAgent");
    }

    [Fact]
    public async Task StartAsync_WithInvalidMarkdown_ShouldSkipAndContinue()
    {
        var invalidContent = "Conteudo invalido sem frontmatter";
        var filePath = Path.Combine(_tempDir, "invalid.md");
        await File.WriteAllTextAsync(filePath, invalidContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agentes = loader.ListarAgentes();
        agentes.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WithEmptyDirectory_ShouldNotThrow()
    {
        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        
        var act = () => loader.StartAsync(CancellationToken.None);
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListarAgentes_WithMultipleAgents_ShouldReturnAll()
    {
        var agent1 = @"---
nome: Agent1
descricao: Primeiro
modelo_alvo: model1
comandos:
  - /a1
---

# Agent1

Persona 1";

        var agent2 = @"---
nome: Agent2
descricao: Segundo
modelo_alvo: model2
comandos:
  - /a2
---

# Agent2

Persona 2";

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "agent1.md"), agent1);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "agent2.md"), agent2);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agentes = loader.ListarAgentes();
        agentes.Should().HaveCount(2);
        agentes.Select(a => a.Nome).Should().Contain(new[] { "Agent1", "Agent2" });
    }

    [Fact]
    public async Task StartAsync_ShouldExtractPersona()
    {
        var markdownContent = @"---
nome: TestAgent
descricao: Test
modelo_alvo: openai/gpt-4
comandos:
  - /test
---

# TestAgent

Voce e um agente de teste.

## Personalidade
- Criativo
- Proativo";

        var filePath = Path.Combine(_tempDir, "test.md");
        await File.WriteAllTextAsync(filePath, markdownContent);

        var loader = new AgenteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var agente = loader.ObterPorComando("/test");
        agente.Should().NotBeNull();
        agente!.Persona.Should().Contain("agente de teste");
        agente.Persona.Should().Contain("Criativo");
    }
}
