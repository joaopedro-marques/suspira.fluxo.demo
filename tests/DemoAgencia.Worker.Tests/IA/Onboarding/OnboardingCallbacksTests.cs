using DemoAgencia.Worker.IA.Onboarding;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Onboarding;

public class OnboardingCallbacksTests
{
    [Fact]
    public void Criar_Etapa_ShouldEncodeCorrectly()
    {
        var callback = OnboardingCallbacks.Criar(OnboardingPasso.Etapa, "pos-compra");

        callback.Should().Be("onb|etapa|pos-compra");
    }

    [Fact]
    public void Criar_Marco_ShouldEncodeCorrectly()
    {
        var callback = OnboardingCallbacks.Criar(OnboardingPasso.Marco, "Pós Financiamento");

        callback.Should().Be("onb|marco|Pós Financiamento");
    }

    [Fact]
    public void Criar_Tema_ShouldEncodeCorrectly()
    {
        var callback = OnboardingCallbacks.Criar(OnboardingPasso.Tema, "Boas vindas");

        callback.Should().Be("onb|tema|Boas vindas");
    }

    [Fact]
    public void Criar_Objetivo_ShouldEncodeCorrectly()
    {
        var callback = OnboardingCallbacks.Criar(OnboardingPasso.Objetivo, "vender");

        callback.Should().Be("onb|objetivo|vender");
    }

    [Fact]
    public void TentarParse_ValidCallback_ShouldReturnTrue()
    {
        var result = OnboardingCallbacks.TentarParse("onb|etapa|pos-compra", out var passo, out var valor);

        result.Should().BeTrue();
        passo.Should().Be(OnboardingPasso.Etapa);
        valor.Should().Be("pos-compra");
    }

    [Fact]
    public void TentarParse_MarcoComEspacos_ShouldReturnTrue()
    {
        var result = OnboardingCallbacks.TentarParse("onb|marco|Pós Financiamento", out var passo, out var valor);

        result.Should().BeTrue();
        passo.Should().Be(OnboardingPasso.Marco);
        valor.Should().Be("Pós Financiamento");
    }

    [Fact]
    public void TentarParse_InvalidFormat_ShouldReturnFalse()
    {
        var result = OnboardingCallbacks.TentarParse("invalid", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TentarParse_WrongPrefix_ShouldReturnFalse()
    {
        var result = OnboardingCallbacks.TentarParse("other|etapa|pos-compra", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TentarParse_InvalidStep_ShouldReturnFalse()
    {
        var result = OnboardingCallbacks.TentarParse("onb|invalid_step|value", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TentarParse_MissingValue_ShouldReturnFalse()
    {
        var result = OnboardingCallbacks.TentarParse("onb|etapa", out _, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("onb|pular")]
    [InlineData("onb|sem-link")]
    [InlineData("onb|confirmar")]
    [InlineData("onb|recomecar")]
    public void EhAcaoEspecial_ValidActions_ShouldReturnTrue(string callback)
    {
        var result = OnboardingCallbacks.EhAcaoEspecial(callback, out var acao);

        result.Should().BeTrue();
        acao.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("onb|etapa|pos-compra")]
    [InlineData("invalid")]
    [InlineData("onb|unknown")]
    public void EhAcaoEspecial_NonSpecialActions_ShouldReturnFalse(string callback)
    {
        var result = OnboardingCallbacks.EhAcaoEspecial(callback, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void ObterAcao_Pular_ShouldReturnPular()
    {
        OnboardingCallbacks.ObterAcao("onb|pular").Should().Be("pular");
    }

    [Fact]
    public void ObterAcao_Confirmar_ShouldReturnConfirmar()
    {
        OnboardingCallbacks.ObterAcao("onb|confirmar").Should().Be("confirmar");
    }

    [Fact]
    public void ObterAcao_Recomecar_ShouldReturnRecomecar()
    {
        OnboardingCallbacks.ObterAcao("onb|recomecar").Should().Be("recomecar");
    }

    [Fact]
    public void ObterAcao_SemLink_ShouldReturnSemLink()
    {
        OnboardingCallbacks.ObterAcao("onb|sem-link").Should().Be("sem-link");
    }

    [Fact]
    public void Pular_Callback_ShouldBeCorrect()
    {
        OnboardingCallbacks.Pular.Should().Be("onb|pular");
    }

    [Fact]
    public void SemLink_Callback_ShouldBeCorrect()
    {
        OnboardingCallbacks.SemLink.Should().Be("onb|sem-link");
    }

    [Fact]
    public void Confirmar_Callback_ShouldBeCorrect()
    {
        OnboardingCallbacks.Confirmar.Should().Be("onb|confirmar");
    }

    [Fact]
    public void Recomecar_Callback_ShouldBeCorrect()
    {
        OnboardingCallbacks.Recomecar.Should().Be("onb|recomecar");
    }

    [Fact]
    public void Criar_LongValue_ShouldRespect64ByteLimit()
    {
        var longValue = "Indicação premiada - parabéns pela chave";
        var callback = OnboardingCallbacks.Criar(OnboardingPasso.Tema, longValue);

        System.Text.Encoding.UTF8.GetByteCount(callback).Should().BeLessThanOrEqualTo(64);
    }
}
