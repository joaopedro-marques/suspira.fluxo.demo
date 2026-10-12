using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepMapaEmocionalEmailTests
{
    private readonly Mock<IServicoChat> _chatMock = new();
    private readonly AgenteDefinicao _agente = new("mapa", "test/model", 0.2, 500, "persona");

    private StepMapaEmocionalEmail CriarStep() => new(_agente, _chatMock.Object, Mock.Of<ILogger<StepMapaEmocionalEmail>>());

    private static PipelineContext ContextoComEstrategia()
    {
        var estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            SubJornada = "Pos Financiamento",
            TemaSelecionado = "Financeiro"
        };
        estrategia.MapaEmocional.Add(new EtapaEmocional { Etapa = "ASSINATURA DO CONTRATO", FatorDecisao = "ER" });
        estrategia.MapaEmocional.Add(new EtapaEmocional { Etapa = "POS VENDA", FatorDecisao = "ER" });
        estrategia.MapaEmocional.Add(new EtapaEmocional { Etapa = "VISTORIA DO APARTAMENTO", FatorDecisao = "ER" });

        return new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "quero saber sobre financiamento",
            Estrategia = estrategia,
            Brief = new Brief("email", "informar sobre financiamento", null, "proprietarios",
                "financiamento", null, new(), new(), "pos-compra", "Pos Financiamento")
        };
    }

    [Fact]
    public async Task ExecutarAsync_WhenEstrategiaNull_ShouldSkipWithoutLlmCall()
    {
        var ctx = new PipelineContext { ChatId = 1, Cliente = "mrv", Estrategia = null };
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WhenMapaEmocionalEmpty_ShouldSkipWithoutLlmCall()
    {
        var ctx = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" }
        };
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Estrategia!.EtapaMapaEmocional.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WhenLlmReturnsValidEtapa_ShouldSetFatorFromMapa()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"etapa\": \"POS VENDA\"}");

        var ctx = ContextoComEstrategia();
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Estrategia!.EtapaMapaEmocional.Should().Be("POS VENDA");
        result.Estrategia.FatorDecisao.Should().Be("ER");
    }

    [Fact]
    public async Task ExecutarAsync_WhenLlmReturnsEtapaNotInMapa_ShouldDegradeGracefully()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"etapa\": \"ETAPA INEXISTENTE\"}");

        var ctx = ContextoComEstrategia();
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Estrategia!.EtapaMapaEmocional.Should().BeNull();
        result.Estrategia.FatorDecisao.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WhenLlmResponseNotJson_ShouldDegradeGracefully()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Não sei qual etapa aplicar");

        var ctx = ContextoComEstrategia();
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Estrategia!.EtapaMapaEmocional.Should().BeNull();
        result.Estrategia.FatorDecisao.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WhenLlmResponseEmpty_ShouldDegradeGracefully()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var ctx = ContextoComEstrategia();
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Estrategia!.EtapaMapaEmocional.Should().BeNull();
        result.Estrategia.FatorDecisao.Should().BeNull();
    }

    [Fact]
    public void MontarPrompt_ShouldIncludeEtapasEContexto()
    {
        var ctx = ContextoComEstrategia();

        var prompt = StepMapaEmocionalEmail.MontarPrompt(ctx);

        prompt.Should().Contain("ASSINATURA DO CONTRATO");
        prompt.Should().Contain("POS VENDA");
        prompt.Should().Contain("VISTORIA DO APARTAMENTO");
        prompt.Should().Contain("pos-compra");
        prompt.Should().Contain("Pos Financiamento");
        prompt.Should().Contain("Financeiro");
        prompt.Should().Contain("financiamento");
    }

    [Fact]
    public async Task ExecutarAsync_WhenLlmResponseWithMarkdownFences_ShouldExtractJson()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n{\"etapa\": \"POS VENDA\"}\n```");

        var ctx = ContextoComEstrategia();
        var step = CriarStep();

        var result = await step.ExecutarAsync(ctx, CancellationToken.None);

        result.Estrategia!.EtapaMapaEmocional.Should().Be("POS VENDA");
        result.Estrategia.FatorDecisao.Should().Be("ER");
    }
}
