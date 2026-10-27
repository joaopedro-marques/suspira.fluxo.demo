using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Router;

namespace DemoAgencia.Worker.IA.Pipelines;

/// <summary>
/// Clona o contexto de uma entrega antes de uma rodada de ajuste, para que uma
/// re-execucao reprovada pelo QA nao corrompa a versao vigente.
/// </summary>
public static class PipelineContextCloner
{
    public static PipelineContext Clonar(PipelineContext origem)
    {
        var clone = new PipelineContext
        {
            ChatId = origem.ChatId,
            Brief = ClonarBrief(origem.Brief),
            MensagemOriginal = origem.MensagemOriginal,
            Cliente = origem.Cliente,
            Estrategia = ClonarEstrategia(origem.Estrategia),
            Marca = origem.Marca,
            LogoSrc = origem.LogoSrc,
            Copy = origem.Copy,
            HeroSrc = origem.HeroSrc,
            BannerSrc = origem.BannerSrc,
            TemplateId = origem.TemplateId,
            Html = origem.Html,
            FixedAssetRefs = new List<string>(origem.FixedAssetRefs),
            QaFeedback = null,
            QaStepAlvo = null,
            QaAprovado = false,
            Refacoes = 0
        };

        clone.Resultado.RespostaFinal = origem.Resultado.RespostaFinal;
        clone.Resultado.Imagens = new List<ImagemGerada>(origem.Resultado.Imagens);
        clone.Resultado.AssetsAnexados = new List<ImagemGerada>(origem.Resultado.AssetsAnexados);
        clone.Resultado.EtapasExecutadas = new List<string>(origem.Resultado.EtapasExecutadas);
        clone.Resultado.QaAprovado = origem.Resultado.QaAprovado;
        clone.Resultado.QaFeedbackFinal = origem.Resultado.QaFeedbackFinal;

        return clone;
    }

    private static Brief ClonarBrief(Brief brief)
    {
        return brief with
        {
            Restricoes = new List<string>(brief.Restricoes),
            Imagens = new List<ImagemBrief>(brief.Imagens),
            LinksExtras = brief.LinksExtras != null ? new List<string>(brief.LinksExtras) : null
        };
    }

    private static EstrategiaEmail? ClonarEstrategia(EstrategiaEmail? estrategia)
    {
        if (estrategia == null)
            return null;

        return new EstrategiaEmail
        {
            Fase = estrategia.Fase,
            FaseDados = estrategia.FaseDados,
            MapaEmocional = new List<Referencias.EtapaEmocional>(estrategia.MapaEmocional),
            SubJornada = estrategia.SubJornada,
            TemaSelecionado = estrategia.TemaSelecionado,
            EtapaMapaEmocional = estrategia.EtapaMapaEmocional,
            FatorDecisao = estrategia.FatorDecisao,
            Regua = estrategia.Regua
        };
    }
}
