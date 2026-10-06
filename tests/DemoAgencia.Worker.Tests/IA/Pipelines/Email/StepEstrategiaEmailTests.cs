using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class StepEstrategiaEmailTests
{
    private readonly Mock<IReferenciasCliente> _refsMock;
    private readonly Mock<ITemplateCatalogo> _catalogoMock;
    private readonly StepEstrategiaEmail _step;

    public StepEstrategiaEmailTests()
    {
        _refsMock = new Mock<IReferenciasCliente>();
        _catalogoMock = new Mock<ITemplateCatalogo>();
        _catalogoMock.Setup(c => c.Selecionar(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns("MRV_html_vistoria");
        _step = new StepEstrategiaEmail(_refsMock.Object, _catalogoMock.Object);
    }

    [Fact]
    public async Task ExecutarAsync_WithEstrategiaEFase_ShouldPopulateContext()
    {
        var estrategia = new EstrategiaCliente { Cliente = "mrv" };
        estrategia.Fases["pos-compra"] = new FaseEstrategia
        {
            Fase = "pos-compra",
            CorPrincipal = "Roxo",
            DescricaoCor = "Transformacao",
            Temas = new List<string> { "Boas vindas", "Financeiro" },
            SubJornadas = new Dictionary<string, List<string>>
            {
                ["Jornada-pos-compra"] = new List<string> { "Pos Financiamento", "6 meses" }
            }
        };
        estrategia.MapaEmocional.Add(new EtapaEmocional
        {
            Etapa = "ASSINATURA DO CONTRATO",
            FatorDecisao = "ER",
            Sentimentos = new List<string> { "ANIMACAO", "FELICIDADE" }
        });
        estrategia.Satisfacoes.Add(new CategoriaSatisfacao { Nome = "Atendimento", Itens = new List<string> { "Geral" } });
        estrategia.Insatisfacoes.Add(new CategoriaSatisfacao { Nome = "Entrega", Itens = new List<string> { "Demora" } });

        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", "Pos Financiamento")
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().NotBeNull();
        result.Estrategia!.Fase.Should().Be("pos-compra");
        result.Estrategia.FaseDados.Should().NotBeNull();
        result.Estrategia.FaseDados!.CorPrincipal.Should().Be("Roxo");
        result.Estrategia.FaseDados.Temas.Should().Contain("Boas vindas");
        result.Estrategia.MapaEmocional.Should().HaveCount(1);
        result.Estrategia.Satisfacoes.Should().HaveCount(1);
        result.Estrategia.Insatisfacoes.Should().HaveCount(1);
        result.Estrategia.SubJornada.Should().Be("Pos Financiamento");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutEstrategia_ShouldSetNull()
    {
        _refsMock.Setup(r => r.ObterEstrategia("acme")).Returns((EstrategiaCliente?)null);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "acme",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null)
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WithoutEtapaJornada_ShouldSetNull()
    {
        var estrategia = new EstrategiaCliente { Cliente = "mrv" };
        estrategia.Fases["pos-compra"] = new FaseEstrategia { Fase = "pos-compra" };
        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), null, null)
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WithFaseInexistente_ShouldSetNull()
    {
        var estrategia = new EstrategiaCliente { Cliente = "mrv" };
        estrategia.Fases["pos-compra"] = new FaseEstrategia { Fase = "pos-compra" };
        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pre-chaves", null)
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_WithoutCliente_ShouldSetNull()
    {
        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = null,
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-compra", null)
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallCatalogoSelecionar()
    {
        var estrategia = new EstrategiaCliente { Cliente = "mrv" };
        estrategia.Fases["pos-chaves"] = new FaseEstrategia { Fase = "pos-chaves" };
        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
        _catalogoMock.Setup(c => c.Selecionar("mrv", "pos-chaves", "vistoria", "quero agendar"))
            .Returns("MRV_html_vistoria");

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "quero agendar",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-chaves", "vistoria")
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        _catalogoMock.Verify(c => c.Selecionar("mrv", "pos-chaves", "vistoria", "quero agendar"), Times.Once);
        result.TemplateId.Should().Be("MRV_html_vistoria");
    }

    [Fact]
    public async Task ExecutarAsync_WithoutStrategy_ShouldStillSelectTemplate()
    {
        _refsMock.Setup(r => r.ObterEstrategia("mrv")).Returns((EstrategiaCliente?)null);
        _catalogoMock.Setup(c => c.Selecionar("mrv", "pos-chaves", null, "teste"))
            .Returns("MRV_html_default");

        var context = new PipelineContext
        {
            ChatId = 1,
            Cliente = "mrv",
            MensagemOriginal = "teste",
            Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>(), "pos-chaves", null)
        };

        var result = await _step.ExecutarAsync(context, CancellationToken.None);

        result.Estrategia.Should().BeNull();
        result.TemplateId.Should().Be("MRV_html_default");
    }
}
