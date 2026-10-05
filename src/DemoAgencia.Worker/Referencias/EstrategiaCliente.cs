using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Referencias;

[ExcludeFromCodeCoverage]
public class EstrategiaCliente
{
    public string Cliente { get; init; } = string.Empty;
    public Dictionary<string, FaseEstrategia> Fases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EtapaEmocional> MapaEmocional { get; init; } = new();
    public List<CategoriaSatisfacao> Satisfacoes { get; init; } = new();
    public List<CategoriaSatisfacao> Insatisfacoes { get; init; } = new();
}

[ExcludeFromCodeCoverage]
public class FaseEstrategia
{
    public string Fase { get; init; } = string.Empty;
    public string? CorPrincipal { get; init; }
    public string? DescricaoCor { get; init; }
    public List<List<string>> CoresHex { get; init; } = new();
    public List<string> Temas { get; init; } = new();
    public Dictionary<string, List<string>> SubJornadas { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

[ExcludeFromCodeCoverage]
public class EtapaEmocional
{
    public string Etapa { get; init; } = string.Empty;
    public string FatorDecisao { get; init; } = string.Empty;
    public List<string> Sentimentos { get; init; } = new();
    public List<string> Resultado { get; init; } = new();
}

[ExcludeFromCodeCoverage]
public class CategoriaSatisfacao
{
    public string Nome { get; init; } = string.Empty;
    public List<string> Itens { get; init; } = new();
}
