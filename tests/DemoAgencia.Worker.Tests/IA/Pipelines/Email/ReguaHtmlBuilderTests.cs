using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Pipelines.Email;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines.Email;

public class ReguaHtmlBuilderTests
{
    private static ReguaJornada CriarRegua()
    {
        return new ReguaJornada
        {
            Etapas = new List<string>
            {
                "Assinatura do Contrato",
                "Financiamento com o banco",
                "Visita a Obra",
                "Vistoria",
                "Entrega das chaves"
            },
            EtapaDestacadaPorFase = new Dictionary<string, string>
            {
                ["pos-compra"] = "Financiamento com o banco",
                ["pre-chaves"] = "Vistoria",
                ["pos-chaves"] = "Entrega das chaves"
            },
            EstiloHtml = new EstiloReguaHtml()
        };
    }

    [Fact]
    public void Construir_ComRegua_DesenhaEtapasECoresPorEstado()
    {
        var estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            Regua = CriarRegua()
        };

        var html = ReguaHtmlBuilder.Construir(estrategia);

        html.Should().Contain("Assinatura do Contrato");
        html.Should().Contain("Financiamento com o banco");
        html.Should().Contain("Visita a Obra");
        html.Should().Contain("Vistoria");
        html.Should().Contain("Entrega das chaves");
        html.Should().Contain("background-color: #FFFFFF", "etapas concluidas usam a cor concluida");
        html.Should().Contain("background-color: #FFDD02", "a etapa atual usa o amarelo");
        html.Should().Contain("background-color: rgba(255, 255, 255, 0.35)", "etapas futuras usam a cor futura");
        html.Should().Contain("color: #FFDD02; font-weight: bold;", "o label da etapa atual fica destacado");
    }

    [Fact]
    public void Construir_UsaCoresConfiguradasNoJson()
    {
        var regua = CriarRegua();
        regua.EstiloHtml = new EstiloReguaHtml
        {
            CorConcluida = "#111111",
            CorAtual = "#222222",
            CorFutura = "#333333",
            CorLabel = "#444444",
            CorLabelAtual = "#555555"
        };
        var estrategia = new EstrategiaEmail { Fase = "pos-compra", Regua = regua };

        var html = ReguaHtmlBuilder.Construir(estrategia);

        html.Should().Contain("background-color: #111111");
        html.Should().Contain("background-color: #222222");
        html.Should().Contain("background-color: #333333");
        html.Should().Contain("color: #444444");
        html.Should().Contain("color: #555555");
    }

    [Fact]
    public void Construir_SemRegua_RetornaVazio()
    {
        ReguaHtmlBuilder.Construir(new EstrategiaEmail { Fase = "pos-compra" }).Should().BeEmpty();
        ReguaHtmlBuilder.Construir(null).Should().BeEmpty();
    }

    [Fact]
    public void Construir_SemFase_RetornaVazio()
    {
        var estrategia = new EstrategiaEmail { Fase = "", Regua = CriarRegua() };

        ReguaHtmlBuilder.Construir(estrategia).Should().BeEmpty();
    }

    [Fact]
    public void DeterminarEtapaDestacada_PorSubJornada_TemPrioridadeSobreFase()
    {
        var estrategia = new EstrategiaEmail
        {
            Fase = "pos-compra",
            SubJornada = "Vistoria Antecipada",
            Regua = CriarRegua()
        };

        var etapa = ReguaHtmlBuilder.DeterminarEtapaDestacada(estrategia, estrategia.Regua!);

        etapa.Should().Be("Vistoria");
    }

    [Fact]
    public void DeterminarEtapaDestacada_SemSubJornada_UsaMapaDaFase()
    {
        var estrategia = new EstrategiaEmail
        {
            Fase = "pre-chaves",
            Regua = CriarRegua()
        };

        var etapa = ReguaHtmlBuilder.DeterminarEtapaDestacada(estrategia, estrategia.Regua!);

        etapa.Should().Be("Vistoria");
    }

    [Fact]
    public void DeterminarEtapaDestacada_FaseDesconhecida_UsaPrimeiraEtapa()
    {
        var estrategia = new EstrategiaEmail
        {
            Fase = "fase-inexistente",
            Regua = CriarRegua()
        };

        var etapa = ReguaHtmlBuilder.DeterminarEtapaDestacada(estrategia, estrategia.Regua!);

        etapa.Should().Be("Assinatura do Contrato");
    }

    [Fact]
    public void Construir_ComEstiloPorFase_UsaOverrideDaFase()
    {
        var regua = CriarRegua();
        regua.EstiloHtml = new EstiloReguaHtml
        {
            CorConcluida = "#444444",
            CorAtual = "#555555",
            CorFutura = "#666666",
            CorLabel = "#777777",
            CorLabelAtual = "#888888"
        };
        regua.EstiloHtmlPorFase["pos-chaves"] = new EstiloReguaHtml
        {
            CorConcluida = "#2B1B00",
            CorAtual = "#784099",
            CorFutura = "rgba(0, 0, 0, 0.25)",
            CorLabel = "#2B1B00",
            CorLabelAtual = "#784099"
        };
        var estrategia = new EstrategiaEmail { Fase = "pos-chaves", Regua = regua };

        var html = ReguaHtmlBuilder.Construir(estrategia);

        html.Should().Contain("background-color: #2B1B00");
        html.Should().Contain("background-color: #784099");
        html.Should().Contain("color: #2B1B00");
        html.Should().Contain("color: #784099");
        html.Should().NotContain("#444444");
    }

    [Fact]
    public void Construir_SemOverrideDaFaseAtual_UsaEstiloGlobal()
    {
        var regua = CriarRegua();
        regua.EstiloHtml = new EstiloReguaHtml
        {
            CorConcluida = "#444444",
            CorAtual = "#555555",
            CorFutura = "#666666",
            CorLabel = "#777777",
            CorLabelAtual = "#888888"
        };
        regua.EstiloHtmlPorFase["pos-chaves"] = new EstiloReguaHtml
        {
            CorConcluida = "#2B1B00",
            CorLabel = "#2B1B00"
        };
        var estrategia = new EstrategiaEmail { Fase = "pos-compra", Regua = regua };

        var html = ReguaHtmlBuilder.Construir(estrategia);

        html.Should().Contain("#444444");
        html.Should().Contain("#777777");
        html.Should().NotContain("#2B1B00");
    }

    [Fact]
    public void Construir_ComFundoClaro_SubstituiCoresQueSumiriam()
    {
        var estrategia = new EstrategiaEmail { Fase = "pos-chaves", Regua = CriarRegua() };

        var html = ReguaHtmlBuilder.Construir(estrategia, new[] { "#FFB719", "#FFDD02" });

        html.Should().Contain("color: #1A1A1A", "labels brancos somem no fundo amarelo");
        html.Should().NotContain("color: #FFFFFF");
        html.Should().Contain("background-color: #1A1A1A", "segmentos claros somem no fundo amarelo");
    }

    [Fact]
    public void AjustarCores_SemFundos_MantemCoresDeclaradas()
    {
        var estilo = new EstiloReguaHtml();

        var ajustado = ReguaHtmlBuilder.AjustarCores(estilo, null);

        ajustado.label.Should().Be(estilo.CorLabel);
        ajustado.labelAtual.Should().Be(estilo.CorLabelAtual);
        ajustado.concluida.Should().Be(estilo.CorConcluida);
        ajustado.atual.Should().Be(estilo.CorAtual);
        ajustado.futura.Should().Be(estilo.CorFutura);
    }

    [Fact]
    public void AjustarCores_ComFundoEscuro_MantemBranco()
    {
        var estilo = new EstiloReguaHtml();

        var ajustado = ReguaHtmlBuilder.AjustarCores(estilo, new[] { "#784099", "#AB40D9" });

        ajustado.label.Should().Be("#FFFFFF");
        ajustado.concluida.Should().Be("#FFFFFF");
    }
}
