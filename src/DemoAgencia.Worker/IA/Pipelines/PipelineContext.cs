namespace DemoAgencia.Worker.IA.Pipelines;

public class PipelineContext
{
    public long ChatId { get; init; }
    public Router.Brief Brief { get; init; } = null!;
    public string MensagemOriginal { get; init; } = string.Empty;
    public string? Cliente { get; init; }

    public EstrategiaEmail? Estrategia { get; set; }
    public MarcaEmail? Marca { get; set; }
    public string? LogoSrc { get; set; }
    public CopyEmailSlots? Copy { get; set; }
    public string? HeroSrc { get; set; }
    public string? Html { get; set; }
    public string? QaFeedback { get; set; }
    public string? QaStepAlvo { get; set; }
    public bool QaAprovado { get; set; }
    public int Refacoes { get; set; }

    public ResultadoPipeline Resultado { get; } = new();
}
