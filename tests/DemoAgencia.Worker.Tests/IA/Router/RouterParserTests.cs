using DemoAgencia.Worker.IA.Router;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Router;

public class RouterParserTests
{
    [Fact]
    public void TentarExtrair_WithProducaoCompleta_ShouldReturnBrief()
    {
        var texto = """
        {
            "tipo": "producao",
            "cliente": "Acme",
            "brief": {
                "canal": "email",
                "objetivo": "vender",
                "publico": "empresarios",
                "oferta": "Black Friday 50% off",
                "tom": "urgente",
                "link": "https://acme.com/promo",
                "restricoes": ["sem emojis", "max 200 palavras"],
                "imagens": [{"papel": "hero", "descricao": "Banner com produto"}]
            }
        }
        """;

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("producao");
        resultado.Cliente.Should().Be("acme");
        resultado.Brief.Should().NotBeNull();
        resultado.Brief!.Canal.Should().Be("email");
        resultado.Brief.Objetivo.Should().Be("vender");
        resultado.Brief.Publico.Should().Be("empresarios");
        resultado.Brief.Oferta.Should().Be("Black Friday 50% off");
        resultado.Brief.Tom.Should().Be("urgente");
        resultado.Brief.Link.Should().Be("https://acme.com/promo");
        resultado.Brief.Restricoes.Should().BeEquivalentTo("sem emojis", "max 200 palavras");
        resultado.Brief.Imagens.Should().HaveCount(1);
        resultado.Brief.Imagens[0].Papel.Should().Be("hero");
        resultado.Brief.Imagens[0].Descricao.Should().Be("Banner com produto");
    }

    [Fact]
    public void TentarExtrair_WithJsonInMarkdownFence_ShouldExtract()
    {
        var texto = "Segue:\n```json\n{\"tipo\": \"conversa\", \"resposta\": \"Ola!\"}\n```";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("conversa");
        resultado.Resposta.Should().Be("Ola!");
    }

    [Fact]
    public void TentarExtrair_WithConversa_ShouldReturnResposta()
    {
        var texto = """{"tipo": "conversa", "resposta": "O resultado da campanha foi bom"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("conversa");
        resultado.Resposta.Should().Be("O resultado da campanha foi bom");
    }

    [Fact]
    public void TentarExtrair_WithEsclarecimento_ShouldReturnPerguntas()
    {
        var texto = """{"tipo": "esclarecimento", "perguntas": ["Qual o publico?", "Qual o objetivo?"]}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("esclarecimento");
        resultado.Perguntas.Should().HaveCount(2);
        resultado.Perguntas.Should().Contain("Qual o publico?");
    }

    [Fact]
    public void TentarExtrair_WithForaContexto_ShouldReturnTipo()
    {
        var texto = """{"tipo": "fora_contexto"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("fora_contexto");
    }

    [Fact]
    public void TentarExtrair_WithClienteUpperCase_ShouldNormalizeToLower()
    {
        var texto = """{"tipo": "producao", "cliente": "ACME Corp", "brief": {"canal": "email"}}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Cliente.Should().Be("acme corp");
    }

    [Fact]
    public void TentarExtrair_WithInvalidJson_ShouldReturnNull()
    {
        var resultado = RouterParser.TentarExtrair("Texto sem JSON valido");

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEmptyString_ShouldReturnNull()
    {
        var resultado = RouterParser.TentarExtrair("");

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithTipoDesconhecido_ShouldReturnNull()
    {
        var texto = """{"tipo": "produzir_video"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithProducaoSemBrief_ShouldReturnNull()
    {
        var texto = """{"tipo": "producao", "cliente": "acme"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithProducaoCanalInvalido_ShouldReturnNull()
    {
        var texto = """{"tipo": "producao", "brief": {"canal": "tiktok"}}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithCamposOpcionaisAusentes_ShouldDefault()
    {
        var texto = """{"tipo": "producao", "brief": {"canal": "email"}}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Cliente.Should().BeNull();
        resultado.Resposta.Should().BeNull();
        resultado.Perguntas.Should().BeEmpty();
        resultado.Brief!.Link.Should().BeNull();
        resultado.Brief.Objetivo.Should().BeNull();
        resultado.Brief.Publico.Should().BeNull();
        resultado.Brief.Oferta.Should().BeNull();
        resultado.Brief.Tom.Should().BeNull();
        resultado.Brief.Restricoes.Should().BeEmpty();
        resultado.Brief.Imagens.Should().BeEmpty();
        resultado.Brief.EtapaJornada.Should().BeNull();
        resultado.Brief.SubJornada.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEtapaJornada_ShouldExtract()
    {
        var texto = """
        {
            "tipo": "producao",
            "cliente": "MRV",
            "brief": {
                "canal": "email",
                "etapa_jornada": "pos-compra",
                "sub_jornada": "Pos Financiamento"
            }
        }
        """;

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Brief.Should().NotBeNull();
        resultado.Brief!.EtapaJornada.Should().Be("pos-compra");
        resultado.Brief.SubJornada.Should().Be("Pos Financiamento");
    }

    [Fact]
    public void TentarExtrair_WithEtapaJornadaComAcento_ShouldNormalize()
    {
        var texto = """
        {
            "tipo": "producao",
            "brief": {
                "canal": "email",
                "etapa_jornada": "pós-compra"
            }
        }
        """;

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Brief!.EtapaJornada.Should().Be("pos-compra");
    }

    [Fact]
    public void TentarExtrair_WithEtapaJornadaInvalida_ShouldReturnNull()
    {
        var texto = """
        {
            "tipo": "producao",
            "brief": {
                "canal": "email",
                "etapa_jornada": "fase-inexistente"
            }
        }
        """;

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Brief!.EtapaJornada.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithMotivo_ShouldExtract()
    {
        var texto = """{"tipo": "fora_contexto", "motivo": "cliente_nao_permitido"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("fora_contexto");
        resultado.Motivo.Should().Be("cliente_nao_permitido");
    }

    [Fact]
    public void TentarExtrair_WithoutMotivo_ShouldBeNull()
    {
        var texto = """{"tipo": "fora_contexto"}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Motivo.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithResponseEnvelope_ShouldUnwrap()
    {
        var texto = """{"response": {"tipo": "conversa", "resposta": "Ola!"}}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("conversa");
        resultado.Resposta.Should().Be("Ola!");
    }

    [Fact]
    public void TentarExtrair_WithClassificacaoAlias_ShouldExtract()
    {
        var texto = """{"classificacao": "esclarecimento", "perguntas": ["Qual o publico?"]}""";

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("esclarecimento");
        resultado.Perguntas.Should().ContainSingle("Qual o publico?");
    }

    [Fact]
    public void TentarExtrair_WithResponseEnvelopeAndClassificacao_ShouldExtract()
    {
        var texto = """
        {
            "response": {
                "classificacao": "esclarecimento",
                "resposta": "",
                "perguntas": ["Qual e o objetivo?", "Para qual etapa?"],
                "brief": null,
                "motivo": null
            }
        }
        """;

        var resultado = RouterParser.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Tipo.Should().Be("esclarecimento");
        resultado.Resposta.Should().Be("");
        resultado.Perguntas.Should().HaveCount(2);
        resultado.Perguntas.Should().Contain("Qual e o objetivo?");
        resultado.Brief.Should().BeNull();
    }
}
