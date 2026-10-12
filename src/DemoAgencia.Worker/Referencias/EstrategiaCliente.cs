using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Referencias;

[ExcludeFromCodeCoverage]
public class EstrategiaCliente
{
    public string Cliente { get; init; } = string.Empty;
    public Dictionary<string, FaseEstrategia> Fases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EtapaEmocional> MapaEmocional { get; init; } = new();
}

[ExcludeFromCodeCoverage]
public class FaseEstrategia
{
    public string Fase { get; init; } = string.Empty;
    public string? CorPrincipal { get; set; }
    public string? DescricaoCor { get; set; }
    public List<List<string>> CoresHex { get; set; } = new();
    public List<string> Temas { get; set; } = new();
    public Dictionary<string, List<string>> SubJornadas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[ExcludeFromCodeCoverage]
public class EtapaEmocional
{
    public string Etapa { get; init; } = string.Empty;
    public string FatorDecisao { get; init; } = string.Empty;
    public List<string> Sentimentos { get; init; } = new();
}
