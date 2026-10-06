using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines;

public class EstrategiaEmail
{
    public string Fase { get; init; } = string.Empty;
    public FaseEstrategia? FaseDados { get; init; }
    public List<EtapaEmocional> MapaEmocional { get; init; } = new();
    public List<CategoriaSatisfacao> Satisfacoes { get; init; } = new();
    public List<CategoriaSatisfacao> Insatisfacoes { get; init; } = new();
    public string? SubJornada { get; init; }
}
