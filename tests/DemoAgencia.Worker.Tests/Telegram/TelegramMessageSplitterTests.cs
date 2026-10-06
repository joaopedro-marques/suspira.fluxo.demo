using DemoAgencia.Worker.Telegram;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramMessageSplitterTests
{
    [Fact]
    public void Dividir_TextoVazio_RetornaListaComTextoVazio()
    {
        var resultado = TelegramMessageSplitter.Dividir("");

        resultado.Should().HaveCount(1);
        resultado[0].Should().Be("");
    }

    [Fact]
    public void Dividir_TextoCurto_RetornaListaComUmElemento()
    {
        var texto = "Mensagem curta";

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(1);
        resultado[0].Should().Be(texto);
    }

    [Fact]
    public void Dividir_TextoExatoNoLimite_RetornaListaComUmElemento()
    {
        var texto = new string('a', 4000);

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(1);
        resultado[0].Should().HaveLength(4000);
    }

    [Fact]
    public void Dividir_TextoAcimaDoLimite_RetornaDuasPartes()
    {
        var texto = new string('a', 4001);

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(2);
        resultado[0].Should().HaveLength(4000);
        resultado[1].Should().HaveLength(1);
    }

    [Fact]
    public void Dividir_TextoComQuebraDeLinha_DivideNaQuebra()
    {
        var linha1 = new string('a', 3990);
        var linha2 = new string('b', 100);
        var texto = $"{linha1}\n{linha2}";

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(2);
        resultado[0].Should().Be(linha1);
        resultado[1].Should().Be(linha2);
    }

    [Fact]
    public void Dividir_TextoComMultiplasQuebras_DivideNaUltimaQuebraDentroDoLimite()
    {
        var linha1 = new string('a', 2000);
        var linha2 = new string('b', 2000);
        var linha3 = new string('c', 100);
        var texto = $"{linha1}\n{linha2}\n{linha3}";

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(2);
        resultado[0].Should().Be(linha1);
        resultado[1].Should().Be($"{linha2}\n{linha3}");
    }

    [Fact]
    public void Dividir_TextoMuitoLongoSemQuebras_DivideEmPartesDe4000()
    {
        var texto = new string('x', 10000);

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(3);
        resultado[0].Should().HaveLength(4000);
        resultado[1].Should().HaveLength(4000);
        resultado[2].Should().HaveLength(2000);
    }

    [Fact]
    public void Dividir_TextoComQuebraNoLimite_NaoCriaParteVazia()
    {
        var linha1 = new string('a', 3999);
        var linha2 = new string('b', 50);
        var texto = $"{linha1}\n{linha2}";

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(2);
        resultado[0].Should().Be(linha1);
        resultado[1].Should().Be(linha2);
    }

    [Fact]
    public void Dividir_TextoComEspacosNoFinal_RemoveEspacosDaPrimeiraParte()
    {
        var texto = new string('a', 3995) + "     \n" + new string('b', 50);

        var resultado = TelegramMessageSplitter.Dividir(texto);

        resultado.Should().HaveCount(2);
        resultado[0].Should().Be(new string('a', 3995));
        resultado[1].Should().EndWith(new string('b', 50));
    }

    [Fact]
    public void Dividir_ComLimiteCustomizado_RespeitaLimite()
    {
        var texto = new string('a', 150);

        var resultado = TelegramMessageSplitter.Dividir(texto, limite: 50);

        resultado.Should().HaveCount(3);
        resultado[0].Should().HaveLength(50);
        resultado[1].Should().HaveLength(50);
        resultado[2].Should().HaveLength(50);
    }

    [Fact]
    public void Dividir_PreservaConteudoTotal()
    {
        var linha1 = "Primeira linha com algum conteúdo";
        var linha2 = "Segunda linha com mais conteúdo";
        var linha3 = "Terceira linha final";
        var texto = $"{linha1}\n{linha2}\n{linha3}";

        var resultado = TelegramMessageSplitter.Dividir(texto, limite: 40);

        var textoReconstruido = string.Join("\n", resultado);
        textoReconstruido.Should().Be(texto);
    }
}
