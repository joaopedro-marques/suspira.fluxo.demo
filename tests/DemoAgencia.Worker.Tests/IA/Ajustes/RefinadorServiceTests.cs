using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ajustes;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Ajustes;

public class RefinadorServiceTests
{
    private readonly Mock<IServicoChat> _chatMock = new();
    private readonly Mock<ITemplateCatalogo> _catalogoMock = new();

    private RefinadorService CriarService() => new(
        new AgenteDefinicao("refinador", "test/model", 0.2, 2000, "persona"),
        _chatMock.Object,
        _catalogoMock.Object,
        Mock.Of<ILogger<RefinadorService>>());

    private static PipelineContext CriarContexto()
    {
        return new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "email pos-compra",
            Brief = new Brief("email", "engajar", "moradores", "suporte", "acolhedor", null, new(), new()),
            Copy = new CopyEmailSlots("Assunto atual", "pre", "Titulo atual", "<p>corpo</p>", "Saiba mais", "https://mrv.com"),
            Estrategia = new EstrategiaEmail { Fase = "pos-compra", TemaSelecionado = "Boas vindas" },
            HeroSrc = "imagens/gerada_1.png",
            TemplateId = "MRV_html_visitatecnica",
            Html = "<html><body><p>corpo</p></body></html>"
        };
    }

    [Theory]
    [InlineData("{\"step_alvo\":\"copy\",\"instrucoes\":\"Deixe o titulo mais direto\"}", "copy")]
    [InlineData("{\"step_alvo\":\"diagramacao\",\"instrucoes\":\"Troque o fundo\"}", "diagramacao")]
    [InlineData("{\"step_alvo\":\"hero\",\"instrucoes\":\"Imagem generica\"}", "hero")]
    [InlineData("{\"step_alvo\":\"template\",\"instrucoes\":\"Trocar template\"}", "template")]
    public void ParseResposta_ShouldNormalizeStepAlvo(string resposta, string expected)
    {
        var plano = RefinadorService.ParseResposta(resposta, "feedback");

        plano.Should().NotBeNull();
        plano!.StepAlvo.Should().Be(expected);
    }

    [Fact]
    public void ParseResposta_WithUnknownStep_ShouldFallbackToCopy()
    {
        var plano = RefinadorService.ParseResposta(
            "{\"step_alvo\":\"outra_coisa\",\"instrucoes\":\"x\"}", "feedback");

        plano!.StepAlvo.Should().Be("copy");
    }

    [Fact]
    public void ParseResposta_WithInvalidJson_ShouldReturnNull()
    {
        RefinadorService.ParseResposta("nao sei", "feedback").Should().BeNull();
    }

    [Fact]
    public void ParseResposta_WithoutInstrucoes_ShouldUseClientFeedback()
    {
        var plano = RefinadorService.ParseResposta(
            "{\"step_alvo\":\"copy\"}", "o titulo ficou muito formal");

        plano!.Instrucoes.Should().Be("o titulo ficou muito formal");
        plano.FeedbackCliente.Should().Be("o titulo ficou muito formal");
    }

    [Fact]
    public void ParseResposta_TemplateWithValidId_ShouldKeepTemplateId()
    {
        var plano = RefinadorService.ParseResposta(
            "{\"step_alvo\":\"template\",\"instrucoes\":\"trocar\",\"template_id\":\"MRV_html_limite_vistoria\"}",
            "feedback",
            new[] { "MRV_html_visitatecnica", "MRV_html_limite_vistoria" });

        plano!.TemplateId.Should().Be("MRV_html_limite_vistoria");
    }

    [Fact]
    public void ParseResposta_TemplateWithUnknownId_ShouldDropTemplateId()
    {
        var plano = RefinadorService.ParseResposta(
            "{\"step_alvo\":\"template\",\"instrucoes\":\"trocar\",\"template_id\":\"inventado\"}",
            "feedback",
            new[] { "MRV_html_visitatecnica" });

        plano!.TemplateId.Should().BeNull();
    }

    [Fact]
    public void ParseResposta_NonTemplateStep_ShouldDropTemplateId()
    {
        var plano = RefinadorService.ParseResposta(
            "{\"step_alvo\":\"copy\",\"instrucoes\":\"x\",\"template_id\":\"MRV_html_visitatecnica\"}",
            "feedback",
            new[] { "MRV_html_visitatecnica" });

        plano!.TemplateId.Should().BeNull();
    }

    [Fact]
    public void MontarPrompt_ShouldIncludeContextoEFeedbackETemplates()
    {
        var prompt = RefinadorService.MontarPrompt(
            CriarContexto(),
            "a imagem nao tem a ver com o tema",
            new[] { "MRV_html_visitatecnica", "MRV_html_limite_vistoria" });

        prompt.Should().Contain("a imagem nao tem a ver com o tema");
        prompt.Should().Contain("Assunto atual");
        prompt.Should().Contain("pos-compra");
        prompt.Should().Contain("MRV_html_visitatecnica");
        prompt.Should().Contain("MRV_html_limite_vistoria");
        prompt.Should().Contain("<p>corpo</p>");
        prompt.Should().Contain("MENOR etapa");
    }

    [Fact]
    public async Task ClassificarAsync_ShouldCallRefinadorTrace()
    {
        _catalogoMock.Setup(c => c.ListarIds("mrv")).Returns(new[] { "MRV_html_visitatecnica" });
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"step_alvo\":\"hero\",\"instrucoes\":\"Troque a imagem\"}");

        var plano = await CriarService().ClassificarAsync(CriarContexto(), "imagem ruim");

        plano.Should().NotBeNull();
        plano!.StepAlvo.Should().Be("hero");
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_refinador", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClassificarAsync_InvalidJson_ShouldRetryThenReturnNull()
    {
        _chatMock.Setup(c => c.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("resposta invalida");

        var plano = await CriarService().ClassificarAsync(CriarContexto(), "sei la");

        plano.Should().BeNull();
        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "email_refinador_retry", It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ListarTemplates_WhenCatalogoReturnsNull_ShouldReturnEmpty()
    {
        _catalogoMock.Setup(c => c.ListarIds(It.IsAny<string>())).Returns((IReadOnlyList<string>)null!);

        CriarService().ListarTemplates("mrv").Should().BeEmpty();
    }
}
