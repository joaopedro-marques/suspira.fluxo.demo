using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.PreFlight;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.PreFlight;

public class ConversaPendenteStoreTests
{
    private readonly ConversaPendenteStore _store;
    private readonly FakeTimeProvider _timeProvider;

    public ConversaPendenteStoreTests()
    {
        _timeProvider = new FakeTimeProvider();
        var options = Options.Create(new PreFlightOptions { TimeoutMinutosPendencia = 15 });
        var logger = new Mock<ILogger<ConversaPendenteStore>>();
        _store = new ConversaPendenteStore(options, _timeProvider, logger.Object);
    }

    [Fact]
    public void Obter_WithoutStoring_ShouldReturnNull()
    {
        var estado = _store.Obter(123);

        estado.Should().BeNull();
    }

    [Fact]
    public void GuardarAndObter_ShouldReturnStoredState()
    {
        var estado = new EstadoPreFlight { ChatId = 123, MensagemOriginal = "criar post" };
        _store.Guardar(123, estado);

        var obtido = _store.Obter(123);

        obtido.Should().NotBeNull();
        obtido!.MensagemOriginal.Should().Be("criar post");
        obtido.ChatId.Should().Be(123);
    }

    [Fact]
    public void Obter_AfterTimeout_ShouldReturnNull()
    {
        var estado = new EstadoPreFlight { ChatId = 123, MensagemOriginal = "teste" };
        _store.Guardar(123, estado);

        _timeProvider.AdvanceMinutes(16);

        var obtido = _store.Obter(123);

        obtido.Should().BeNull();
    }

    [Fact]
    public void Obter_BeforeTimeout_ShouldReturnState()
    {
        var estado = new EstadoPreFlight { ChatId = 123, MensagemOriginal = "teste" };
        _store.Guardar(123, estado);

        _timeProvider.AdvanceMinutes(14);

        var obtido = _store.Obter(123);

        obtido.Should().NotBeNull();
    }

    [Fact]
    public void Remover_AfterStoring_ShouldReturnNullOnObter()
    {
        var estado = new EstadoPreFlight { ChatId = 123, MensagemOriginal = "teste" };
        _store.Guardar(123, estado);

        _store.Remover(123);

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void Guardar_ShouldOverwritePreviousState()
    {
        _store.Guardar(123, new EstadoPreFlight { ChatId = 123, MensagemOriginal = "v1" });
        _store.Guardar(123, new EstadoPreFlight { ChatId = 123, MensagemOriginal = "v2" });

        var obtido = _store.Obter(123);

        obtido!.MensagemOriginal.Should().Be("v2");
    }

    [Fact]
    public void Obter_ExpiredState_ShouldRemoveFromStore()
    {
        _store.Guardar(123, new EstadoPreFlight { ChatId = 123 });
        _timeProvider.AdvanceMinutes(16);
        _store.Obter(123).Should().BeNull();

        _timeProvider.Reset();
        _store.Obter(123).Should().BeNull();
    }

    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void AdvanceMinutes(int minutes)
        {
            _now = _now.AddMinutes(minutes);
        }

        public void Reset()
        {
            _now = DateTimeOffset.UtcNow;
        }
    }
}
