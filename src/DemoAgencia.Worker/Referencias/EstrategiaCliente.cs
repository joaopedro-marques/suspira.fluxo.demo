using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.Referencias;

[ExcludeFromCodeCoverage]
public class EstrategiaCliente
{
    public string Cliente { get; init; } = string.Empty;
    public Dictionary<string, FaseEstrategia> Fases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EtapaEmocional> MapaEmocional { get; init; } = new();
    public ReguaJornada? Regua { get; set; }
    public FasesConfig? ConfiguracaoFases { get; set; }

    /// <summary>Configuracao do header declarada em {CLIENTE}_header.json.</summary>
    public HeaderConfig? Header { get; set; }
    
    public string? NormalizarFase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (ConfiguracaoFases == null || ConfiguracaoFases.Fases.Count == 0)
            return null;

        var inputNormalized = FaseJornada.NormalizarTexto(input);

        foreach (var kvp in ConfiguracaoFases.Fases)
        {
            kvp.Value.FaseKey = kvp.Key;
            if (kvp.Value.Matches(inputNormalized))
                return kvp.Key;
        }

        return null;
    }
}

[ExcludeFromCodeCoverage]
public class FaseEstrategia
{
    public string Fase { get; init; } = string.Empty;
    public string? CorPrincipal { get; set; }
    public string? DescricaoCor { get; set; }
    public List<List<string>> CoresHex { get; set; } = new();

    /// <summary>Cor do titulo sobre o header desta fase. Ausente = escolhida por contraste.</summary>
    public string? CorTextoHeader { get; set; }

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
