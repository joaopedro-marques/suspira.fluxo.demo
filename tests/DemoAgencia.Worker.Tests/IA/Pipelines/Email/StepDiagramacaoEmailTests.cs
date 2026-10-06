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

public class StepDiagramacaoEmailTests
{
    private readonly Mock<IServicoChat> _chatMock;
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<IIconDescricaoCache> _iconCacheMock;
    private readonly StepDiagramacaoEmail _step;
    private readonly AgenteDefinicao _agente;

    public StepDiagramacaoEmailTests()
    {
        _chatMock = new Mock<IServicoChat>();
        _refsMock = new Mock<IReferenciasCliente>();
        _iconCacheMock = new Mock<IIconDescricaoCache>();
        _iconCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IconDescricao("Icone teste", new List<string> { "teste" }, "Flat"));

        _agente = new AgenteDefinicao("diagramador", "test/model", 0.6, 8000, "persona");
        _step = new StepDiagramacaoEmail(_agente, _chatMock.Object, _refsMock.Object, _iconCacheMock.Object, Mock.Of<ILogger<StepDiagramacaoEmail>>());
    }

    private static PipelineContext CriarContexto(string corpo = "<p>Corpo original</p>")
    {
        return new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = new CopyEmailSlots("Assunto", "Preheader", "Titulo", "Saudacao", corpo, "CTA", "https://link", "Rodape"),
            Estrategia = new EstrategiaEmail
            {
                Fase = "pos-chaves",
                FaseDados = new FaseEstrategia { Fase = "pos-chaves", CorPrincipal = "#006b40" }
            }
        };
    }

    [Fact]
    public async Task ExecutarAsync_WithValidHtml_ShouldReplaceCorpo()
    {
        var diagrammed = """<table width="600" border="0"><tr><td style="padding: 20px;"><p style="font-size: 14px;">Conteudo diagramado</p></td></tr></table>""";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diagrammed);

        var context = CriarContexto();
        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Copy!.Corpo.Should().Be(diagrammed);
    }

    [Fact]
    public async Task ExecutarAsync_WithDivTag_ShouldKeepOriginalCorpo()
    {
        var invalidHtml = """<div style="padding: 20px;"><p>Invalid layout</p></div>""";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidHtml);

        var context = CriarContexto("<p>Original</p>");
        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Copy!.Corpo.Should().Be("<p>Original</p>");
    }

    [Fact]
    public async Task ExecutarAsync_WithStyleBlock_ShouldKeepOriginalCorpo()
    {
        var invalidHtml = """<style>.red { color: red; }</style><table><tr><td>Test</td></tr></table>""";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidHtml);

        var context = CriarContexto("<p>Original</p>");
        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Copy!.Corpo.Should().Be("<p>Original</p>");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutTable_ShouldKeepOriginalCorpo()
    {
        var noTable = """<p style="font-size: 14px;">Sem tabela</p>""";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noTable);

        var context = CriarContexto("<p>Original</p>");
        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Copy!.Corpo.Should().Be("<p>Original</p>");
    }

    [Fact]
    public async Task ExecutarAsync_WithNullCopy_ShouldNotCallLLM()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>()),
            Copy = null
        };

        context = await _step.ExecutarAsync(context, CancellationToken.None);

        _chatMock.Verify(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MontarPromptAsync_ShouldIncludeStrategyAndIcons()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "casa", Caminho = "path/casa.png" },
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "telefone", Caminho = "path/telefone.png" }
        });

        var context = CriarContexto();
        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(context, _refsMock.Object, _iconCacheMock.Object);

        prompt.Should().Contain("#006b40");
        prompt.Should().Contain("pos-chaves");
        prompt.Should().Contain("casa");
        prompt.Should().Contain("telefone");
        prompt.Should().Contain("Corpo original");
    }

    [Fact]
    public void IsValidHtml_WithDivTag_ShouldReturnFalse()
    {
        StepDiagramacaoEmail.IsValidHtml("<div>test</div>").Should().BeFalse();
    }

    [Fact]
    public void IsValidHtml_WithStyleBlock_ShouldReturnFalse()
    {
        StepDiagramacaoEmail.IsValidHtml("<style>.x{}</style><table><tr><td></td></tr></table>").Should().BeFalse();
    }

    [Fact]
    public void IsValidHtml_WithScriptTag_ShouldReturnFalse()
    {
        StepDiagramacaoEmail.IsValidHtml("<script>alert(1)</script><table><tr><td></td></tr></table>").Should().BeFalse();
    }

    [Fact]
    public void IsValidHtml_WithoutTable_ShouldReturnFalse()
    {
        StepDiagramacaoEmail.IsValidHtml("<p style=\"color:red;\">No table</p>").Should().BeFalse();
    }

    [Fact]
    public void IsValidHtml_WithValidTableHtml_ShouldReturnTrue()
    {
        StepDiagramacaoEmail.IsValidHtml("""<table width="600" border="0"><tr><td style="padding: 20px;"><p>OK</p></td></tr></table>""").Should().BeTrue();
    }

    [Fact]
    public async Task MontarPrompt_ShouldIncludeIconDescriptionsAndExtensions()
    {
        var iconCacheMock = new Mock<IIconDescricaoCache>();
        iconCacheMock.Setup(c => c.ObterDescricaoAsync(It.IsAny<AssetVisual>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IconDescricao("Icone de casa", new List<string> { "moradia", "imovel" }, "Flat line"));

        var step = new StepDiagramacaoEmail(_agente, _chatMock.Object, _refsMock.Object, iconCacheMock.Object, Mock.Of<ILogger<StepDiagramacaoEmail>>());

        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "casa", Caminho = "path/MRV_casa.png" }
        });

        var context = CriarContexto();
        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(context, _refsMock.Object, iconCacheMock.Object);

        prompt.Should().Contain("assets/casa.png");
        prompt.Should().Contain("Icone de casa");
        prompt.Should().Contain("moradia");
        prompt.Should().NotContain("Referencie como assets/{nome}");
    }

    [Fact]
    public async Task MontarPrompt_WithQaFeedback_ShouldIncludeRevisionSection()
    {
        var context = CriarContexto();
        context.Refacoes = 1;
        context.QaFeedback = "Corrigir duplicacao de saudacao";

        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(context, _refsMock.Object, _iconCacheMock.Object);

        prompt.Should().Contain("REVISAO NECESSARIA");
        prompt.Should().Contain("refacao 1");
        prompt.Should().Contain("Corrigir duplicacao de saudacao");
    }

    [Fact]
    public async Task MontarPrompt_WithoutQaFeedback_ShouldNotIncludeRevisionSection()
    {
        var context = CriarContexto();

        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(context, _refsMock.Object, _iconCacheMock.Object);

        prompt.Should().NotContain("REVISAO NECESSARIA");
    }

    [Fact]
    public async Task MontarPrompt_ShouldNotIncludeSaudacao()
    {
        var context = CriarContexto();

        var prompt = await StepDiagramacaoEmail.MontarPromptAsync(context, _refsMock.Object, _iconCacheMock.Object);

        prompt.Should().NotContain("Saudacao:");
    }

    [Theory]
    [InlineData("<p>Ola, %%NOME%%!</p><table><tr><td>rest</td></tr></table>", "<table><tr><td>rest</td></tr></table>")]
    [InlineData("<p style=\"color:red;\">Ol&aacute;, %%NOME%%!</p><table><tr><td>ok</td></tr></table>", "<table><tr><td>ok</td></tr></table>")]
    [InlineData("<table><tr><td>sem saudacao</td></tr></table>", "<table><tr><td>sem saudacao</td></tr></table>")]
    public void StripPrimeiraSaudacao_ShouldRemoveOnlyFirstGreeting(string input, string expected)
    {
        StepDiagramacaoEmail.StripPrimeiraSaudacao(input).Should().Be(expected);
    }

    [Fact]
    public async Task ExecutarAsync_WithSaudacaoInHtml_ShouldStripIt()
    {
        var diagrammed = """<p>Ola, %%NOME%%!</p><table width="600" border="0"><tr><td style="padding: 20px;"><p style="font-size: 14px;">Conteudo</p></td></tr></table>""";
        _chatMock.Setup(c => c.ChamarAgenteAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diagrammed);

        var context = CriarContexto();
        context = await _step.ExecutarAsync(context, CancellationToken.None);

        context.Copy!.Corpo.Should().NotContain("%%NOME%%");
        context.Copy.Corpo.Should().Contain("Conteudo");
    }
}
