using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.Seguranca;

public class RateLimiterServiceTests
{
    [Fact]
    public void PodeProcessar_FirstMessage_ShouldReturnTrue()
    {
        var service = new RateLimiterService(TestOptions.Create(new SegurancaOptions { MaxMensagensPorMinuto = 5 }));
        service.PodeProcessar(123).Should().BeTrue();
    }

    [Fact]
    public void PodeProcessar_WithinLimit_ShouldReturnTrue()
    {
        var service = new RateLimiterService(TestOptions.Create(new SegurancaOptions { MaxMensagensPorMinuto = 5 }));
        for (int i = 0; i < 4; i++)
            service.PodeProcessar(123);

        service.PodeProcessar(123).Should().BeTrue();
    }

    [Fact]
    public void PodeProcessar_ExceedsLimit_ShouldReturnFalse()
    {
        var service = new RateLimiterService(TestOptions.Create(new SegurancaOptions { MaxMensagensPorMinuto = 3 }));
        service.PodeProcessar(123);
        service.PodeProcessar(123);
        service.PodeProcessar(123);

        service.PodeProcessar(123).Should().BeFalse();
    }

    [Fact]
    public void PodeProcessar_DifferentChats_ShouldBeIndependent()
    {
        var service = new RateLimiterService(TestOptions.Create(new SegurancaOptions { MaxMensagensPorMinuto = 2 }));
        service.PodeProcessar(111);
        service.PodeProcessar(111);

        service.PodeProcessar(222).Should().BeTrue();
    }
}
