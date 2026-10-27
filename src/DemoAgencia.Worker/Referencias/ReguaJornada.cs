namespace DemoAgencia.Worker.Referencias;

public class ReguaJornada
{
    public List<string> Etapas { get; set; } = new();
    public Dictionary<string, string> EtapaDestacadaPorFase { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public EstiloRegua Estilo { get; set; } = new();
    public EstiloReguaHtml EstiloHtml { get; set; } = new();
}

public class EstiloRegua
{
    public string Concluida { get; set; } = "line WHITE fill on the progress line segment";
    public string Atual { get; set; } = "solid YELLOW highlight (yellow fill, yellow marker, or yellow circle indicator)";
    public string Futura { get; set; } = "outline only, no fill, empty/vacant segments";
    public List<string> Diretrizes { get; set; } = new();
}

/// <summary>Cores usadas para desenhar a regua de jornada em HTML no header do email.</summary>
public class EstiloReguaHtml
{
    public string CorConcluida { get; set; } = "#FFFFFF";
    public string CorAtual { get; set; } = "#FFDD02";
    public string CorFutura { get; set; } = "rgba(255, 255, 255, 0.35)";
    public string CorLabel { get; set; } = "#FFFFFF";
    public string CorLabelAtual { get; set; } = "#FFDD02";
}
