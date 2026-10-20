using DemoAgencia.Worker.IA.Onboarding;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Router;

public class BriefExtractor
{
    private static readonly string[] ObjetivosValidos = { "vender", "engajar", "informar", "engajar", "vendas", "engajamento", "informação", "informar" };

    public CamposExtraidos Extrair(string mensagem, EstrategiaCliente? estrategia)
    {
        if (string.IsNullOrWhiteSpace(mensagem))
            return new CamposExtraidos();

        var mensagemLower = mensagem.ToLowerInvariant();
        var etapa = ExtrairEtapa(mensagemLower, estrategia);
        var marco = etapa != null && estrategia != null ? ExtrairMarco(mensagemLower, estrategia, etapa) : null;
        var tema = etapa != null && estrategia != null ? ExtrairTema(mensagemLower, estrategia, etapa) : null;
        var objetivo = ExtrairObjetivo(mensagemLower);

        return new CamposExtraidos(
            EtapaJornada: etapa,
            Marco: marco,
            Tema: tema,
            Objetivo: objetivo);
    }

    private string? ExtrairEtapa(string mensagemLower, EstrategiaCliente? estrategia)
    {
        if (estrategia != null)
        {
            // Usar vocabulário do cliente se disponível
            var faseNormalizada = estrategia.NormalizarFase(mensagemLower);
            if (!string.IsNullOrEmpty(faseNormalizada))
                return faseNormalizada;
        }
        
        // Fallback para normalização padrão quando não há configuração de fases
        return FaseJornada.Normalizar(mensagemLower);
    }

    private string? ExtrairMarco(string mensagemLower, EstrategiaCliente estrategia, string etapa)
    {
        if (!estrategia.Fases.TryGetValue(etapa, out var fase))
            return null;

        foreach (var (subJornada, marcos) in fase.SubJornadas)
        {
            foreach (var marco in marcos)
            {
                if (mensagemLower.Contains(marco, StringComparison.OrdinalIgnoreCase))
                    return marco;
            }
        }

        return null;
    }

    private string? ExtrairTema(string mensagemLower, EstrategiaCliente estrategia, string etapa)
    {
        if (!estrategia.Fases.TryGetValue(etapa, out var fase))
            return null;

        foreach (var tema in fase.Temas)
        {
            if (mensagemLower.Contains(tema, StringComparison.OrdinalIgnoreCase))
                return tema;
        }

        return null;
    }

    private string? ExtrairObjetivo(string mensagemLower)
    {
        if (mensagemLower.Contains("vender") || mensagemLower.Contains("vendas") || mensagemLower.Contains("venda"))
            return "vender";
        if (mensagemLower.Contains("engajar") || mensagemLower.Contains("engajamento") || mensagemLower.Contains("engajar"))
            return "engajar";
        if (mensagemLower.Contains("informar") || mensagemLower.Contains("informação") || mensagemLower.Contains("informar"))
            return "informar";

        return null;
    }
}
