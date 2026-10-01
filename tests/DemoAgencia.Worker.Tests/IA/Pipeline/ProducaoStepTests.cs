using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipeline;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipeline;

public class ProducaoStepTests
{
    private readonly Mock<ILogger<ProducaoStep>> _loggerMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly ProducaoStep _step;

    public ProducaoStepTests()
    {
        _loggerMock = new Mock<ILogger<ProducaoStep>>();

        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());

        var anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
        anonimizadorMock.Setup(x => x.Anonimizar(It.IsAny<string>())).Returns<string>(s => s);

        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object,
            anonimizadorMock.Object);

        _openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object,
            Mock.Of<IHttpClientFactory>());

        _step = new ProducaoStep(
            _loggerMock.Object,
            _openRouterMock.Object);
    }

    private static AgenteDefinicao CriarRedator() => new()
    {
        Nome = "Redator",
        ModeloAlvo = "claude",
        Persona = "persona",
        Papel = "producao",
        Tipo = "texto"
    };

    private static AgenteDefinicao CriarEditorImagens() => new()
    {
        Nome = "EditorImagens",
        ModeloAlvo = "dall-e",
        Persona = "persona",
        Papel = "producao",
        Tipo = "imagem"
    };

    private static PipelineContext CriarContext(AgenteDefinicao? agente = null) => new()
    {
        ChatId = 123,
        Mensagem = "crie um post",
        Rota = "pipeline",
        AgenteProducao = agente ?? CriarRedator(),
        InstrucoesProducao = "Escreva um post sobre marketing",
        CancellationToken = CancellationToken.None
    };

    [Fact]
    public async Task ExecutarAsync_TextAgent_ShouldReturnOutput()
    {
        var redator = CriarRedator();

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "producao_Redator", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Post criado com sucesso sobre marketing");

        var context = CriarContext(redator);
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.OutputProducao.Should().Be("Post criado com sucesso sobre marketing");
        context.Resultado.EtapasExecutadas.Should().Contain("producao_Redator");
    }

    [Fact]
    public async Task ExecutarAsync_ImageAgent_ShouldGenerateImage()
    {
        var editor = CriarEditorImagens();
        var imagemBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "producao_EditorImagens_enriquecimento", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prompt otimizado para imagem");

        _openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(imagemBytes);

        var context = CriarContext(editor);
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.Resultado.Imagem.Should().BeEquivalentTo(imagemBytes);
        context.Resultado.LegendaImagem.Should().Be("crie um post");
        context.OutputProducao.Should().Contain("Imagem gerada com sucesso");
        context.OutputProducao.Should().Contain("prompt otimizado");
        context.Resultado.EtapasExecutadas.Should().Contain("producao_EditorImagens");
    }

    [Fact]
    public async Task ExecutarAsync_ImageAgent_FailedGeneration_ShouldSetFailureOutput()
    {
        var editor = CriarEditorImagens();

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "producao_EditorImagens_enriquecimento", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prompt otimizado");

        _openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var context = CriarContext(editor);
        var result = await _step.ExecutarAsync(context);

        result.DeveContinuar.Should().BeTrue();
        context.OutputProducao.Should().Be("Falha ao gerar imagem.");
        context.Resultado.Imagem.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var redator = CriarRedator();

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "producao_Redator", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Output");

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            AgenteProducao = redator,
            InstrucoesProducao = "instrucoes",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("✍️ Produzindo com Redator...");
    }

    [Fact]
    public async Task ExecutarAsync_ImageAgent_ShouldNotifyImageGenerationProgress()
    {
        var editor = CriarEditorImagens();
        var imagemBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prompt otimizado");

        _openRouterMock
            .Setup(x => x.GerarImagemAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(imagemBytes);

        var progressMessages = new List<string>();
        var context = new PipelineContext
        {
            ChatId = 123,
            Mensagem = "teste",
            Rota = "pipeline",
            AgenteProducao = editor,
            InstrucoesProducao = "instrucoes",
            OnProgresso = msg => { progressMessages.Add(msg); return Task.CompletedTask; },
            CancellationToken = CancellationToken.None
        };

        await _step.ExecutarAsync(context);

        progressMessages.Should().Contain("🎨 Gerando imagem...");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldPassInstrucoesToAgent()
    {
        var redator = CriarRedator();

        string? capturedInstrucoes = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "producao_Redator", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => capturedInstrucoes = instrucoes)
            .ReturnsAsync("Output");

        var context = CriarContext(redator);
        context.InstrucoesProducao = "Instrucoes especificas para o redator";
        await _step.ExecutarAsync(context);

        capturedInstrucoes.Should().Be("Instrucoes especificas para o redator");
    }
}
