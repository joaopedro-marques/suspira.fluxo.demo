using DemoAgencia.Worker.Referencias;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.Referencias;

public class CorHexTests
{
    [Theory]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("#784099", "#784099")]
    [InlineData("  #Ab40d9 ", "#AB40D9")]
    public void TryNormalizar_ComHexValido_ExpandeENormaliza(string entrada, string esperado)
    {
        CorHex.TryNormalizar(entrada, out var cor).Should().BeTrue();
        cor.Should().Be(esperado);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("784099")]
    [InlineData("#GGGGGG")]
    [InlineData("amarelo")]
    [InlineData("rgba(0, 0, 0, 0.25)")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalizar_ComValorInvalido_RetornaFalse(string? entrada)
    {
        CorHex.TryNormalizar(entrada, out var cor).Should().BeFalse();
        cor.Should().BeEmpty();
    }

    [Fact]
    public void Contraste_PretoComBranco_Retorna21()
    {
        CorHex.Contraste("#000000", "#FFFFFF").Should().BeApproximately(21, 0.01);
    }

    [Fact]
    public void Contraste_CoresIguais_Retorna1()
    {
        CorHex.Contraste("#784099", "#784099").Should().BeApproximately(1, 0.01);
    }

    [Fact]
    public void Contraste_ComCorInvalida_RetornaZero()
    {
        CorHex.Contraste("#GGGGGG", "#FFFFFF").Should().Be(0);
    }

    [Fact]
    public void EscolherTexto_FundoClaroSemPreferida_UsaEscuro()
    {
        CorHex.EscolherTexto(new[] { "#FFB719", "#FFDD02" }).Should().Be(CorHex.TextoEscuroPadrao);
    }

    [Fact]
    public void EscolherTexto_FundoEscuroSemPreferida_UsaClaro()
    {
        CorHex.EscolherTexto(new[] { "#784099", "#AB40D9" }).Should().Be(CorHex.TextoClaroPadrao);
    }

    [Fact]
    public void EscolherTexto_PreferidaLegivel_MantemCorDeclarada()
    {
        CorHex.EscolherTexto(new[] { "#784099", "#AB40D9" }, "#FFFFFF")
            .Should().Be(CorHex.TextoClaroPadrao);
    }

    [Fact]
    public void EscolherTexto_PreferidaIlegivel_TrocaPelaCorLegivel()
    {
        CorHex.EscolherTexto(new[] { "#FFB719", "#FFDD02" }, "#FFFFFF")
            .Should().Be(CorHex.TextoEscuroPadrao);
    }

    [Fact]
    public void EscolherTexto_SemFundos_UsaClaro()
    {
        CorHex.EscolherTexto(Array.Empty<string>()).Should().Be(CorHex.TextoClaroPadrao);
    }

    [Fact]
    public void EscolherVisivel_PreferidaQueSomeNoFundo_UsaCorLegivel()
    {
        CorHex.EscolherVisivel(new[] { "#FFB719", "#FFDD02" }, "#FFFFFF")
            .Should().Be(CorHex.TextoEscuroPadrao);
    }

    [Fact]
    public void EscolherVisivel_PreferidaVisivel_Mantem()
    {
        CorHex.EscolherVisivel(new[] { "#784099", "#AB40D9" }, "#FFFFFF")
            .Should().Be(CorHex.TextoClaroPadrao);
    }

    [Fact]
    public void ContrasteMinimo_UsaOPiorStopDoGradiente()
    {
        var minimo = CorHex.ContrasteMinimo(new[] { "#F7297D", "#FF5AAD" }, "#FFFFFF");

        minimo.Should().BeApproximately(CorHex.Contraste("#FF5AAD", "#FFFFFF"), 0.01);
    }
}
