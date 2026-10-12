using DemoAgencia.Worker.IA.Router;

namespace DemoAgencia.Worker.IA.Onboarding;

public class OnboardingState
{
    public long ChatId { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string MensagemOriginal { get; set; } = string.Empty;
    public OnboardingPasso PassoAtual { get; set; } = OnboardingPasso.Etapa;
    public string? EtapaJornada { get; set; }
    public string? Marco { get; set; }
    public string? Tema { get; set; }
    public string? Objetivo { get; set; }
    public string? Oferta { get; set; }
    public string? Publico { get; set; }
    public string? CtaLink { get; set; }
    public bool CtaLinkDefinidoExplicitamente { get; set; }
    public List<string> LinksExtras { get; set; } = new();
    public List<MaterialCliente> Materiais { get; set; } = new();
    public Brief? BriefOriginal { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
}
