using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Ajustes;
using DemoAgencia.Worker.IA.Pipelines;
using DemoAgencia.Worker.IA.Router;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Ajustes;

public class EntregaStoreTests
{
    private readonly EntregaStore _store;
    private readonly FakeTimeProvider _timeProvider;

    public EntregaStoreTests()
    {
        _timeProvider = new FakeTimeProvider();
        var options = Options.Create(new AjustesOptions { TtlHorasEntrega = 24 });
        _store = new EntregaStore(options, _timeProvider, Mock.Of<ILogger<EntregaStore>>());
    }

    [Fact]
    public void Obter_WithoutStoring_ShouldReturnNull()
    {
        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void GuardarAndObter_ShouldReturnStoredState()
    {
        _store.Guardar(123, CriarEstado(versao: 1));

        var entrega = _store.Obter(123);

        entrega.Should().NotBeNull();
        entrega!.Versao.Should().Be(1);
        entrega.Fase.Should().Be(FaseAjuste.AposEntrega);
    }

    [Fact]
    public void Obter_AfterTtl_ShouldReturnNull()
    {
        _store.Guardar(123, CriarEstado());

        _timeProvider.AdvanceHours(25);

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void Obter_BeforeTtl_ShouldReturnState()
    {
        _store.Guardar(123, CriarEstado());

        _timeProvider.AdvanceHours(23);

        _store.Obter(123).Should().NotBeNull();
    }

    [Fact]
    public void Remover_AfterStoring_ShouldReturnNullOnObter()
    {
        _store.Guardar(123, CriarEstado());

        _store.Remover(123);

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void Guardar_ShouldOverwritePreviousState()
    {
        _store.Guardar(123, CriarEstado(versao: 1));
        _store.Guardar(123, CriarEstado(versao: 2));

        _store.Obter(123)!.Versao.Should().Be(2);
    }

    private static EstadoEntrega CriarEstado(int versao = 1)
    {
        return new EstadoEntrega
        {
            Contexto = new PipelineContext
            {
                ChatId = 123,
                Brief = new Brief("email", null, null, null, null, null, new List<string>(), new List<ImagemBrief>())
            },
            Versao = versao
        };
    }

    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void AdvanceHours(int hours)
        {
            _now = _now.AddHours(hours);
        }
    }
}
