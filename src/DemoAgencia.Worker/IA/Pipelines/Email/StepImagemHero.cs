using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepImagemHero : IPipelineStep
{
    public string Nome => "hero";

    private readonly AgenteDefinicao _agentePrompt;
    private readonly IServicoChat _servicoChat;
    private readonly IGeradorImagem _geradorImagem;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly ITemplateCatalogo _templateCatalogo;
    private readonly IBannerDescricaoCache _bannerCache;

    public StepImagemHero(
        AgenteDefinicao agentePrompt,
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem,
        ITemplateCatalogo templateCatalogo,
        IBannerDescricaoCache bannerCache)
    {
        _agentePrompt = agentePrompt;
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _templateCatalogo = templateCatalogo;
        _bannerCache = bannerCache;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var cliente = context.Cliente;
        var fase = context.Estrategia?.Fase;
        var subJornada = context.Estrategia?.SubJornada ?? context.Brief.SubJornada;

        var templateSubJornadas = !string.IsNullOrEmpty(context.TemplateId)
            ? _templateCatalogo.ObterSubJornadas(context.TemplateId)
            : null;

        var banner = _referencias.SelecionarBanner(
            cliente ?? "", fase, subJornada, templateSubJornadas, context.MensagemOriginal);

        if (banner != null && File.Exists(banner.Caminho))
        {
            var bytes = await File.ReadAllBytesAsync(banner.Caminho, ct);
            var legenda = $"{banner.Nome}{Path.GetExtension(banner.Caminho)}";
            context.BannerSrc = $"assets/{legenda}";
            if (!context.Resultado.AssetsAnexados.Any(a => a.Legenda == legenda))
                context.Resultado.AssetsAnexados.Add(new ImagemGerada(bytes, legenda));
            context.HeroSrc = null;
            return context;
        }

        var heroBrief = context.Brief.Imagens.FirstOrDefault(i =>
            i.Papel.Equals("hero", StringComparison.OrdinalIgnoreCase) ||
            i.Papel.Equals("banner", StringComparison.OrdinalIgnoreCase));

        if (heroBrief == null)
        {
            context.HeroSrc = null;
            return context;
        }

        var promptBase = await ConstruirPromptBase(heroBrief.Descricao, cliente, context.Estrategia, ct);
        
        if (context.Refacoes > 0 && !string.IsNullOrEmpty(context.QaFeedback) && 
            context.QaStepAlvo?.Equals("hero", StringComparison.OrdinalIgnoreCase) == true)
        {
            promptBase += $"\n\n⚠️ REVISION REQUIRED (retry {context.Refacoes})\n";
            promptBase += $"QA rejected the previous version. Fix the following issues:\n";
            promptBase += $"{context.QaFeedback}\n";
        }
        
        var promptFinal = await GerarPromptDetalhado(context.ChatId, promptBase, ct);

        var resultado = await _geradorImagem.GerarImagemAsync(context.ChatId, promptFinal, ct);
        if (!resultado.Sucesso || resultado.Bytes == null)
        {
            context.HeroSrc = null;
            return context;
        }

        var indice = context.Resultado.Imagens.Count + 1;
        context.Resultado.Imagens.Add(new ImagemGerada(resultado.Bytes, $"Hero image {indice}"));
        context.HeroSrc = $"imagens/gerada_{indice}.png";

        return context;
    }

    internal virtual async Task<string> ConstruirPromptBase(string descricao, string? cliente, EstrategiaEmail? estrategia, CancellationToken ct)
    {
        var prompt = descricao;

        if (estrategia?.FaseDados != null)
        {
            var fase = estrategia.FaseDados;
            if (!string.IsNullOrEmpty(fase.CorPrincipal))
            {
                prompt += $"\n\nColor palette for this journey phase ({estrategia.Fase}):";
                prompt += $"\nMain color: {fase.CorPrincipal}";
                if (!string.IsNullOrEmpty(fase.DescricaoCor))
                    prompt += $" ({fase.DescricaoCor})";
                if (fase.CoresHex.Count > 0)
                {
                    var hexList = fase.CoresHex.SelectMany(h => h).Where(c => c.StartsWith("#"));
                    if (hexList.Any())
                        prompt += $"\nHex codes: {string.Join(", ", hexList)}";
                }
            }
        }

        if (string.IsNullOrEmpty(cliente))
            return prompt;

        var assets = _referencias.ListarAssets(cliente);
        var marcas = assets.Where(a => a.Tipo == TipoAsset.Logo || a.Tipo == TipoAsset.Icon).ToList();
        var banners = assets.Where(a => a.Tipo == TipoAsset.Banner).ToList();

        var descricoes = new List<string>();
        foreach (var asset in marcas)
        {
            try
            {
                if (File.Exists(asset.Caminho))
                {
                    var bytes = await File.ReadAllBytesAsync(asset.Caminho, ct);
                    var descricaoAsset = await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
                    if (!string.IsNullOrEmpty(descricaoAsset))
                        descricoes.Add($"{asset.Tipo}: {descricaoAsset}");
                }
            }
            catch { }
        }

        foreach (var banner in banners)
        {
            try
            {
                if (File.Exists(banner.Caminho))
                {
                    var descricaoBanner = await _bannerCache.ObterDescricaoAsync(banner, ct);
                    if (!string.IsNullOrEmpty(descricaoBanner.DescricaoGeral))
                        descricoes.Add($"Banner ({banner.Nome}): {descricaoBanner.ToPromptSection()}");
                }
            }
            catch { }
        }

        if (descricoes.Count > 0)
            prompt += "\n\nVisual identity references:\n" + string.Join("\n", descricoes);

        return prompt;
    }

    private async Task<string> GerarPromptDetalhado(long chatId, string promptBase, CancellationToken ct)
    {
        var resposta = await _servicoChat.ChamarAgenteAsync(
            chatId,
            _agentePrompt.Persona,
            _agentePrompt.Modelo,
            promptBase,
            "email_hero_prompt",
            temperature: _agentePrompt.Temperatura,
            maxTokens: _agentePrompt.MaxTokens,
            ct: ct);

        return resposta?.Trim() ?? promptBase;
    }
}
