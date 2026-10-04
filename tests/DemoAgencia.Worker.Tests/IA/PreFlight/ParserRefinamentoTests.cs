using DemoAgencia.Worker.IA.PreFlight;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.PreFlight;

public class ParserRefinamentoTests
{
    [Fact]
    public void TentarExtrair_WithPrecisaEsclarecimento_ShouldReturnQuestions()
    {
        var texto = """{"precisa_esclarecimento": true, "perguntas": ["Qual o publico?", "Para qual plataforma?"]}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.PrecisaEsclarecimento.Should().BeTrue();
        resultado.Perguntas.Should().HaveCount(2);
        resultado.Perguntas.Should().Contain("Qual o publico?");
        resultado.Perguntas.Should().Contain("Para qual plataforma?");
    }

    [Fact]
    public void TentarExtrair_WithPedidoRefinado_ShouldReturnRefinedRequest()
    {
        var texto = """{"precisa_esclarecimento": false, "pedido_refinado": "Criar post para Instagram", "cliente": "acme", "simples": false}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.PrecisaEsclarecimento.Should().BeFalse();
        resultado.PedidoRefinado.Should().Be("Criar post para Instagram");
        resultado.Cliente.Should().Be("acme");
        resultado.Simples.Should().BeFalse();
    }

    [Fact]
    public void TentarExtrair_WithSimplesTrue_ShouldMarkSimple()
    {
        var texto = """{"precisa_esclarecimento": false, "pedido_refinado": "Ola", "simples": true}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Simples.Should().BeTrue();
        resultado.PedidoRefinado.Should().Be("Ola");
    }

    [Fact]
    public void TentarExtrair_WithJsonInMarkdown_ShouldExtract()
    {
        var texto = "Aqui esta:\n```json\n{\"precisa_esclarecimento\": false, \"pedido_refinado\": \"post\", \"simples\": true}\n```";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.PrecisaEsclarecimento.Should().BeFalse();
        resultado.PedidoRefinado.Should().Be("post");
    }

    [Fact]
    public void TentarExtrair_WithInvalidJson_ShouldReturnNull()
    {
        var texto = "Texto sem JSON valido";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithEmptyString_ShouldReturnNull()
    {
        var texto = "";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithoutCliente_ShouldReturnNullCliente()
    {
        var texto = """{"precisa_esclarecimento": false, "pedido_refinado": "post", "simples": false}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Cliente.Should().BeNull();
    }

    [Fact]
    public void TentarExtrair_WithoutPerguntas_ShouldReturnEmptyList()
    {
        var texto = """{"precisa_esclarecimento": true, "perguntas": []}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Perguntas.Should().BeEmpty();
    }

    [Fact]
    public void TentarExtrair_WithMissingPerguntasField_ShouldReturnEmptyList()
    {
        var texto = """{"precisa_esclarecimento": true}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Perguntas.Should().BeEmpty();
    }

    [Fact]
    public void TentarExtrair_WithMissingSimples_ShouldDefaultFalse()
    {
        var texto = """{"precisa_esclarecimento": false, "pedido_refinado": "post"}""";

        var resultado = ParserRefinamento.TentarExtrair(texto);

        resultado.Should().NotBeNull();
        resultado!.Simples.Should().BeFalse();
    }
}
