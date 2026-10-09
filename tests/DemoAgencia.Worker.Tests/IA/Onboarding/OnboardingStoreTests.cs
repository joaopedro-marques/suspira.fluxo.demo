using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Onboarding;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Onboarding;

public class OnboardingStoreTests
{
    private readonly OnboardingStore _store;
    private readonly FakeTimeProvider _timeProvider;

    public OnboardingStoreTests()
    {
        _timeProvider = new FakeTimeProvider();
        var options = Options.Create(new PreFlightOptions { TimeoutMinutosPendencia = 15 });
        var logger = new Mock<ILogger<OnboardingStore>>();
        _store = new OnboardingStore(options, _timeProvider, logger.Object);
    }

    [Fact]
    public void Obter_WithoutStoring_ShouldReturnNull()
    {
        var state = _store.Obter(123);

        state.Should().BeNull();
    }

    [Fact]
    public void GuardarAndObter_ShouldReturnStoredState()
    {
        var state = new OnboardingState
        {
            ChatId = 123,
            Cliente = "mrv",
            MensagemOriginal = "criar email",
            PassoAtual = OnboardingPasso.Etapa
        };
        _store.Guardar(123, state);

        var obtido = _store.Obter(123);

        obtido.Should().NotBeNull();
        obtido!.ChatId.Should().Be(123);
        obtido.Cliente.Should().Be("mrv");
        obtido.PassoAtual.Should().Be(OnboardingPasso.Etapa);
    }

    [Fact]
    public void Obter_AfterTimeout_ShouldReturnNull()
    {
        var state = new OnboardingState { ChatId = 123, Cliente = "mrv" };
        _store.Guardar(123, state);

        _timeProvider.AdvanceMinutes(16);

        var obtido = _store.Obter(123);

        obtido.Should().BeNull();
    }

    [Fact]
    public void Obter_BeforeTimeout_ShouldReturnState()
    {
        var state = new OnboardingState { ChatId = 123, Cliente = "mrv" };
        _store.Guardar(123, state);

        _timeProvider.AdvanceMinutes(14);

        var obtido = _store.Obter(123);

        obtido.Should().NotBeNull();
    }

    [Fact]
    public void Remover_AfterStoring_ShouldReturnNullOnObter()
    {
        var state = new OnboardingState { ChatId = 123, Cliente = "mrv" };
        _store.Guardar(123, state);

        _store.Remover(123);

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void Guardar_ShouldOverwritePreviousState()
    {
        _store.Guardar(123, new OnboardingState { ChatId = 123, Cliente = "mrv", PassoAtual = OnboardingPasso.Etapa });
        _store.Guardar(123, new OnboardingState { ChatId = 123, Cliente = "mrv", PassoAtual = OnboardingPasso.Marco });

        var obtido = _store.Obter(123);

        obtido!.PassoAtual.Should().Be(OnboardingPasso.Marco);
    }

    [Fact]
    public void Obter_ExpiredState_ShouldRemoveFromStore()
    {
        _store.Guardar(123, new OnboardingState { ChatId = 123 });
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
