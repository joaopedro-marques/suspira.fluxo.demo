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

public class StepAjusteTests
{
    private readonly Mock<IServicoChat> _chatMock = new();
    private readonly Mock<IGeradorImagem> _geradorMock = new();
    private readonly Mock<IReferenciasCliente> _refsMock = new();
    private readonly Mock<IAnalisadorImagem> _analisadorMock = new();
    private readonly Mock<ITemplateCatalogo> _catalogoMock = new();
    private readonly Mock<IBannerDescricaoCache> _bannerCacheMock = new();
    private readonly Mock<IIconDescricaoCache> _iconCacheMock = new();

    private static PipelineContext CriarContexto()
    {
        return new PipelineContext
        {
            ChatId = 1,
            Cliente = null,
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", "engajar", null, null, null, null, new(), new()),
            Copy = new CopyEmailSlots("Assunto antigo", "pre", "Titulo antigo", "<p>corpo antigo</p>", "CTA", "https://link"),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" },
            FeedbackAjuste = "Pedido do cliente: o titulo ficou formal. Instrucoes: deixe mais acolhedor."
        };
    }

    [Fact]
    public void StepCopyEmail_ComFeedbackAjuste_DeveInjetarNoPrompt()
    {
        var prompt = StepCopyEmail.MontarPrompt(CriarContexto());

        prompt.Should().Contain("Ajuste solicitado pelo cliente");
        prompt.Should().Contain("o titulo ficou formal");
        prompt.Should().Contain("Copy anterior");
        prompt.Should().Contain("Assunto antigo");
    }

    [Fact]
    public void StepCopyEmail_SemFeedbackAjuste_NaoDeveInjetarSecao()
    {
        var contexto = CriarContexto();
        contexto.FeedbackAjuste = null;

        var prompt = StepCopyEmail.MontarPrompt(contexto);

        prompt.Should().NotContain("Ajuste solicitado pelo cliente");
    }

    [Fact]
    public async Task StepDiagramacaoEmail_ComFeedbackAjuste_DeveInjetarNoPrompt()
    {
        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(
            CriarContexto(), _refsMock.Object, _iconCacheMock.Object);

        prompt.Should().Contain("Ajuste solicitado pelo cliente");
        prompt.Should().Contain("o titulo ficou formal");
        prompt.Should().Contain("Altere APENAS o que foi pedido");
    }

    [Fact]
    public async Task StepImagemHero_PreservarHero_NaoDeveChamarGerador()
    {
        var step = CriarHeroStep();
        var contexto = CriarContexto();
        contexto.PreservarHero = true;
        contexto.HeroSrc = "imagens/gerada_1.png";

        var resultado = await step.ExecutarAsync(contexto, CancellationToken.None);

        resultado.HeroSrc.Should().Be("imagens/gerada_1.png");
        _geradorMock.Verify(g => g.GerarImagemAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StepImagemHero_ComFeedbackAjuste_DeveInjetarNoPromptDeGeracao()
    {
        _refsMock.Setup(r => r.ListarAssets(It.IsAny<string>())).Returns(new List<AssetVisual>());
        _refsMock.Setup(r => r.SelecionarBannersRanked(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<string?>()))
            .Returns(new List<AssetVisual>());

        string? promptLlm = null;
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), "email_hero_prompt", It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, string, string, string, double, int, CancellationToken>(
                (_, _, _, instrucoes, _, _, _, _) => promptLlm = instrucoes)
            .ReturnsAsync("prompt detalhado");

        string? promptGeracao = null;
        _geradorMock.Setup(g => g.GerarImagemAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, CancellationToken>((_, prompt, _) => promptGeracao = prompt)
            .ReturnsAsync(new ResultadoImagem(new byte[] { 0x89, 0x50 }, null));

        var contexto = new PipelineContext
        {
            ChatId = 1,
            Cliente = null,
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", "engajar", null, null, null, null, new(),
                new List<ImagemBrief> { new("hero", "Banner de assembleia") }),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra" },
            FeedbackAjuste = "Pedido do cliente: o titulo ficou formal. Instrucoes: deixe mais acolhedor."
        };

        var step = CriarHeroStep();
        await step.ExecutarAsync(contexto, CancellationToken.None);

        promptLlm.Should().NotBeNull();
        promptLlm!.Should().Contain("Client requested adjustment");
        promptLlm.Should().Contain("o titulo ficou formal");
        promptGeracao.Should().NotBeNull();
    }

    private StepImagemHero CriarHeroStep()
    {
        return new StepImagemHero(
            new AgenteDefinicao("hero", "test/model", 0.7, 1000, "persona"),
            new AgenteDefinicao("curador", "test/model", 0.2, 1000, "curador"),
            _chatMock.Object,
            _geradorMock.Object,
            _refsMock.Object,
            _analisadorMock.Object,
            _catalogoMock.Object,
            _bannerCacheMock.Object,
            _iconCacheMock.Object,
            Mock.Of<ILogger<StepImagemHero>>());
    }
}
