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
        var etapa = ExtrairEtapa(mensagemLower);
        var marco = etapa != null && estrategia != null ? ExtrairMarco(mensagemLower, estrategia, etapa) : null;
        var tema = etapa != null && estrategia != null ? ExtrairTema(mensagemLower, estrategia, etapa) : null;
        var objetivo = ExtrairObjetivo(mensagemLower);

        return new CamposExtraidos(
            EtapaJornada: etapa,
            Marco: marco,
            Tema: tema,
            Objetivo: objetivo);
    }

    private string? ExtrairEtapa(string mensagemLower)
    {
        if (mensagemLower.Contains("pós-compra") || mensagemLower.Contains("pos-compra") || mensagemLower.Contains("pós compra") || mensagemLower.Contains("pos compra"))
            return "pos-compra";

        if (mensagemLower.Contains("pré-chaves") || mensagemLower.Contains("pre-chaves") || mensagemLower.Contains("pré chaves") || mensagemLower.Contains("pre chaves"))
            return "pre-chaves";

        if (mensagemLower.Contains("pós-chaves") || mensagemLower.Contains("pos-chaves") || mensagemLower.Contains("pós chaves") || mensagemLower.Contains("pos chaves"))
            return "pos-chaves";

        return null;
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
