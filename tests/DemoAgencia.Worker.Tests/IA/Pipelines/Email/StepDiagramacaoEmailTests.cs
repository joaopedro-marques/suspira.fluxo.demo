using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepDiagramacaoEmailTests
{
    private readonly Mock<IServicoChat> _chatMock;
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly StepDiagramacaoEmail _step;
    private readonly AgenteDefinicao _agente;

    public StepDiagramacaoEmailTests()
    {
        _chatMock = new Mock<IServicoChat>();
        _refsMock = new Mock<IReferenciasCliente>();

        _agente = new AgenteDefinicao("diagramador", "test/model", 0.6, 8000, "persona");
        _step = new StepDiagramacaoEmail(_agente, _chatMock.Object, _refsMock.Object);
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
    public async Task MontarPrompt_ShouldIncludeStrategyAndIcons()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>
        {
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "casa", Caminho = "path/casa.png" },
            new() { Cliente = "mrv", Tipo = TipoAsset.Icon, Nome = "telefone", Caminho = "path/telefone.png" }
        });

        var context = CriarContexto();
        var prompt = StepDiagramacaoEmail.MontarPrompt(context, _refsMock.Object);

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
}
