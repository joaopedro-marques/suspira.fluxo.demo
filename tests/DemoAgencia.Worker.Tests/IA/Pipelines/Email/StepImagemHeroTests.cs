using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepImagemHeroTests
{
    private readonly Mock<IServicoChat> _chatMock;
    private readonly Mock<IGeradorImagem> _geradorMock;
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly StepImagemHero _step;

    public StepImagemHeroTests()
    {
        _chatMock = new Mock<IServicoChat>();
        _geradorMock = new Mock<IGeradorImagem>();
        _refsMock = new Mock<IReferenciasCliente>();
        _analisadorMock = new Mock<IAnalisadorImagem>();

        var agente = new DemoAgencia.Worker.Agentes.AgenteDefinicao("hero", "test/model", 0.7, 1000, "persona");
        _step = new StepImagemHero(agente, _chatMock.Object, _geradorMock.Object, _refsMock.Object, _analisadorMock.Object);
    }

    [Fact]
    public async Task ConstruirPromptBase_WithEstrategia_ShouldIncludePalette()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>());

        var estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            FaseDados = new FaseEstrategia
            {
                Fase = "pos-compra",
                CorPrincipal = "Roxo",
                DescricaoCor = "Transformacao e sonho",
                CoresHex = new List<List<string>> { new() { "#784099" }, new() { "#AB40D9" } }
            }
        };

        var prompt = await _step.ConstruirPromptBase("A professional banner", "mrv", estrategia, CancellationToken.None);

        prompt.Should().Contain("Color palette");
        prompt.Should().Contain("Roxo");
        prompt.Should().Contain("Transformacao");
        prompt.Should().Contain("#784099");
        prompt.Should().Contain("#AB40D9");
        prompt.Should().Contain("pos-compra");
    }

    [Fact]
    public async Task ConstruirPromptBase_WithoutEstrategia_ShouldNotIncludePalette()
    {
        _refsMock.Setup(r => r.ListarAssets("mrv")).Returns(new List<AssetVisual>());

        var prompt = await _step.ConstruirPromptBase("A professional banner", "mrv", null, CancellationToken.None);

        prompt.Should().NotContain("Color palette");
        prompt.Should().Be("A professional banner");
    }

    [Fact]
    public async Task ConstruirPromptBase_WithoutCliente_ShouldReturnDescription()
    {
        var prompt = await _step.ConstruirPromptBase("A professional banner", null, null, CancellationToken.None);

        prompt.Should().Be("A professional banner");
    }
}
