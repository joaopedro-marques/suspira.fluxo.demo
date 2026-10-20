using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines;

public class EstrategiaEmail
{
    public string Fase { get; init; } = string.Empty;
    public FaseEstrategia? FaseDados { get; init; }
    public List<EtapaEmocional> MapaEmocional { get; init; } = new();
    public string? SubJornada { get; init; }
    public string? TemaSelecionado { get; init; }
    public string? EtapaMapaEmocional { get; set; }
    public string? FatorDecisao { get; set; }
    public ReguaJornada? Regua { get; init; }
}
