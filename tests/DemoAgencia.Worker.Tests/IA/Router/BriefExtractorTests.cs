using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Router;

public class BriefExtractorTests
{
    private readonly BriefExtractor _extractor;
    private readonly EstrategiaCliente _estrategiaMrv;

    public BriefExtractorTests()
    {
        _extractor = new BriefExtractor();
        _estrategiaMrv = CriarEstrategiaMrv();
    }

    [Theory]
    [InlineData("email pós-compra MRV", "pos-compra")]
    [InlineData("pos-compra", "pos-compra")]
    [InlineData("pós compra", "pos-compra")]
    [InlineData("email pré-chaves", "pre-chaves")]
    [InlineData("pre-chaves", "pre-chaves")]
    [InlineData("pré chaves", "pre-chaves")]
    [InlineData("pós-chaves", "pos-chaves")]
    [InlineData("pos chaves", "pos-chaves")]
    public void Extrair_ComEtapa_DeveRetornarEtapaCorreta(string mensagem, string etapaEsperada)
    {
        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.EtapaJornada.Should().Be(etapaEsperada);
    }

    [Theory]
    [InlineData("pós financiamento MRV pós-compra", "Pós Financiamento")]
    [InlineData("email sobre vistoria antecipada pré-chaves", "Vistoria Antecipada")]
    [InlineData("6 meses pós chaves", "6 meses Pós chaves")]
    public void Extrair_ComMarco_DeveRetornarMarcoCorreto(string mensagem, string marcoEsperado)
    {
        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.Marco.Should().Be(marcoEsperado);
    }

    [Theory]
    [InlineData("boas vindas MRV pós-compra", "Boas vindas")]
    [InlineData("email sobre assistência técnica pós-chaves", "Assistência técnica")]
    [InlineData("implantação de condomínio pré-chaves", "Implantação de condomínio")]
    public void Extrair_ComTema_DeveRetornarTemaCorreto(string mensagem, string temaEsperado)
    {
        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.Tema.Should().Be(temaEsperado);
    }

    [Theory]
    [InlineData("vender email MRV", "vender")]
    [InlineData("email para engajar clientes", "engajar")]
    [InlineData("informar sobre entrega", "informar")]
    public void Extrair_ComObjetivo_DeveRetornarObjetivoCorreto(string mensagem, string objetivoEsperado)
    {
        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.Objetivo.Should().Be(objetivoEsperado);
    }

    [Fact]
    public void Extrair_ComTodosCampos_DeveRetornarCamposExtraidos()
    {
        var mensagem = "email pós-compra MRV sobre boas vindas para vender";

        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.EtapaJornada.Should().Be("pos-compra");
        resultado.Tema.Should().Be("Boas vindas");
        resultado.Objetivo.Should().Be("vender");
    }

    [Fact]
    public void Extrair_MensagemVazia_DeveRetornarCamposNulos()
    {
        var resultado = _extractor.Extrair("", _estrategiaMrv);

        resultado.EtapaJornada.Should().BeNull();
        resultado.Marco.Should().BeNull();
        resultado.Tema.Should().BeNull();
        resultado.Objetivo.Should().BeNull();
    }

    [Fact]
    public void Extrair_SemEstrategia_DeveExtrairApenasEtapaEOobjetivo()
    {
        var mensagem = "email pós-compra para vender";

        var resultado = _extractor.Extrair(mensagem, null);

        resultado.EtapaJornada.Should().Be("pos-compra");
        resultado.Marco.Should().BeNull();
        resultado.Tema.Should().BeNull();
        resultado.Objetivo.Should().Be("vender");
    }

    [Fact]
    public void Extrair_MarcoIncompativelComEtapa_DeveRetornarNull()
    {
        var mensagem = "pós-compra sobre vistoria antecipada";

        var resultado = _extractor.Extrair(mensagem, _estrategiaMrv);

        resultado.EtapaJornada.Should().Be("pos-compra");
        resultado.Marco.Should().BeNull();
    }

    private static EstrategiaCliente CriarEstrategiaMrv()
    {
        return new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = new FaseEstrategia
                {
                    Fase = "pos-compra",
                    CorPrincipal = "Roxo",
                    Temas = new List<string> { "Boas vindas", "Indicação premiada" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-pos-compra"] = new List<string> { "Pós Financiamento", "6 meses Pós Financiamento" }
                    }
                },
                ["pre-chaves"] = new FaseEstrategia
                {
                    Fase = "pre-chaves",
                    CorPrincipal = "Rosa",
                    Temas = new List<string> { "Financeiro", "Implantação de condomínio" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-pre-chaves"] = new List<string> { "Assembleia de condomínio", "Vistoria Antecipada", "Entrega das chaves" }
                    }
                },
                ["pos-chaves"] = new FaseEstrategia
                {
                    Fase = "pos-chaves",
                    CorPrincipal = "Amarelo",
                    Temas = new List<string> { "Canais", "Assistência técnica" },
                    SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Jornada-do-produto-morador"] = new List<string> { "6 meses Pós chaves", "18 meses Pós chaves" }
                    }
                }
            }
        };
    }
}
