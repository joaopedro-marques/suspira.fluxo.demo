namespace DemoAgencia.Worker.IA.Onboarding;

public record PerguntaOnboarding(
    string Texto,
    OnboardingTipoPasso Tipo,
    List<OpcaoOnboarding> Opcoes);

public record OpcaoOnboarding(string Label, string CallbackData);
