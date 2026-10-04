using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class ParserDecisaoTests
{
    [Fact]
    public void TentarExtrair_WithValidJson_ShouldReturnDecisao()
    {
        var texto = "{\"acao\": \"responder_direto\", \"resposta\": \"Olá\"}";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().NotBeNull();
        decisao!.Acao.Should().Be("responder_direto");
        decisao.Resposta.Should().Be("Olá");
    }

    [Fact]
    public void TentarExtrair_WithJsonInMarkdown_ShouldExtract()
    {
        var texto = "Aqui está:\n```json\n{\"acao\": \"fora_contexto\"}\n```";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().NotBeNull();
        decisao!.Acao.Should().Be("fora_contexto");
    }

    [Fact]
    public void TentarExtrair_WithInvalidJson_ShouldReturnNull()
    {
        var texto = "Texto sem JSON válido";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEmptyString_ShouldReturnNull()
    {
        var decisao = ParserDecisao.TentarExtrair("");

        decisao.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithAllFields_ShouldParseAll()
    {
        var texto = "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva\", \"cliente\": \"acme\"}";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().NotBeNull();
        decisao!.Acao.Should().Be("chamar_agente");
        decisao.Agente.Should().Be("Redator");
        decisao.Briefing.Should().Be("Escreva");
        decisao.Cliente.Should().Be("acme");
    }

    [Fact]
    public void TentarExtrair_WithMissingAcao_ShouldReturnNull()
    {
        var texto = "{\"resposta\": \"Olá\"}";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().BeNull();
    }

    [Fact]
    public void ExtrairJson_WithValidJson_ShouldReturnJson()
    {
        var texto = "Texto {\"key\": \"value\"} mais texto";

        var json = ParserDecisao.ExtrairJson(texto);

        json.Should().Be("{\"key\": \"value\"}");
    }

    [Fact]
    public void ExtrairJson_WithNoBraces_ShouldReturnNull()
    {
        var json = ParserDecisao.ExtrairJson("sem chaves");

        json.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithArtefatosArray_ShouldParseIds()
    {
        var texto = "{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"monte\", \"artefatos\": [\"art_1\", \"art_3\"]}";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().NotBeNull();
        decisao!.ArtefatosIds.Should().BeEquivalentTo("art_1", "art_3");
    }

    [Fact]
    public void TentarExtrair_WithoutArtefatos_ShouldReturnEmpty()
    {
        var texto = "{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"monte\"}";

        var decisao = ParserDecisao.TentarExtrair(texto);

        decisao.Should().NotBeNull();
        decisao!.ArtefatosIds.Should().BeEmpty();
    }
}
