using System.Text.Json;
using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepImagemHero : IPipelineStep
{
    public string Nome => "hero";

    private const int MaxCandidatosBanner = 3;

    private readonly AgenteDefinicao _agentePrompt;
    private readonly AgenteDefinicao _agenteCurador;
    private readonly IServicoChat _servicoChat;
    private readonly IGeradorImagem _geradorImagem;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly ITemplateCatalogo _templateCatalogo;
    private readonly IBannerDescricaoCache _bannerCache;
    private readonly IIconDescricaoCache _iconCache;

    public StepImagemHero(
        AgenteDefinicao agentePrompt,
        AgenteDefinicao agenteCurador,
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem,
        ITemplateCatalogo templateCatalogo,
        IBannerDescricaoCache bannerCache,
        IIconDescricaoCache iconCache)
    {
        _agentePrompt = agentePrompt;
        _agenteCurador = agenteCurador;
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _templateCatalogo = templateCatalogo;
        _bannerCache = bannerCache;
        _iconCache = iconCache;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var isHeroRefacao = context.Refacoes > 0 &&
            context.QaStepAlvo?.Equals("hero", StringComparison.OrdinalIgnoreCase) == true;

        var bannerRejeitado = false;

        if (!isHeroRefacao)
        {
            var cliente = context.Cliente;
            var fase = context.Estrategia?.Fase;
            var subJornada = context.Estrategia?.SubJornada ?? context.Brief.SubJornada;

            var templateSubJornadas = !string.IsNullOrEmpty(context.TemplateId)
                ? _templateCatalogo.ObterSubJornadas(context.TemplateId)
                : null;

            var candidatos = _referencias.SelecionarBannersRanked(
                cliente ?? "", fase, subJornada, templateSubJornadas, context.MensagemOriginal, MaxCandidatosBanner);

            if (candidatos.Count > 0)
            {
                foreach (var candidato in candidatos)
                {
                    var compativel = await ValidarBannerCompativelAsync(context.ChatId, candidato, context, ct);

                    if (compativel == false)
                        continue;

                    if (File.Exists(candidato.Caminho))
                    {
                        var bytes = await File.ReadAllBytesAsync(candidato.Caminho, ct);
                        var legenda = $"{candidato.Nome}{Path.GetExtension(candidato.Caminho)}";
                        context.BannerSrc = $"assets/{legenda}";
                        if (!context.Resultado.AssetsAnexados.Any(a => a.Legenda == legenda))
                            context.Resultado.AssetsAnexados.Add(new ImagemGerada(bytes, legenda));
                        context.HeroSrc = null;
                        return context;
                    }
                }

                bannerRejeitado = true;
            }
        }

        var heroBrief = context.Brief.Imagens.FirstOrDefault(i =>
            i.Papel.Equals("hero", StringComparison.OrdinalIgnoreCase) ||
            i.Papel.Equals("banner", StringComparison.OrdinalIgnoreCase));

        string descricaoBase;

        if (heroBrief != null)
        {
            descricaoBase = heroBrief.Descricao;
        }
        else if (bannerRejeitado)
        {
            descricaoBase = ConstruirDescricaoDefault(context);
        }
        else
        {
            context.HeroSrc = null;
            context.BannerSrc = null;
            return context;
        }

        var promptBase = await ConstruirPromptBase(descricaoBase, context.Cliente, context.Estrategia, ct);

        if (isHeroRefacao && !string.IsNullOrEmpty(context.QaFeedback))
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
            context.BannerSrc = null;
            return context;
        }

        var indice = context.Resultado.Imagens.Count + 1;
        context.Resultado.Imagens.Add(new ImagemGerada(resultado.Bytes, $"Hero image {indice}"));
        context.HeroSrc = $"imagens/gerada_{indice}.png";
        context.BannerSrc = null;

        return context;
    }

    internal virtual async Task<bool?> ValidarBannerCompativelAsync(long chatId, AssetVisual banner, PipelineContext context, CancellationToken ct)
    {
        try
        {
            var descricao = await _bannerCache.ObterDescricaoAsync(banner, ct);
            if (string.IsNullOrEmpty(descricao.DescricaoGeral))
                return null;

            var prompt = MontarPromptCurador(descricao, context);
            var resposta = await _servicoChat.ChamarAgenteAsync(
                chatId,
                _agenteCurador.Persona,
                _agenteCurador.Modelo,
                prompt,
                "email_banner_check",
                temperature: _agenteCurador.Temperatura,
                maxTokens: _agenteCurador.MaxTokens,
                ct: ct);

            var (compativel, _) = ParseCuradorResult(resposta);
            return compativel;
        }
        catch
        {
            return null;
        }
    }

    internal static string MontarPromptCurador(BannerDescricao descricao, PipelineContext context)
    {
        var prompt = $"## Descricao do banner\n{descricao.ToPromptSection()}\n";
        prompt += $"\n## Tema do email\n";
        if (!string.IsNullOrEmpty(context.Brief.Objetivo))
            prompt += $"Objetivo: {context.Brief.Objetivo}\n";
        if (!string.IsNullOrEmpty(context.Brief.Oferta))
            prompt += $"Oferta: {context.Brief.Oferta}\n";
        if (!string.IsNullOrEmpty(context.Estrategia?.Fase))
            prompt += $"Fase: {context.Estrategia.Fase}\n";
        if (!string.IsNullOrEmpty(context.Estrategia?.SubJornada))
            prompt += $"Sub-jornada: {context.Estrategia.SubJornada}\n";
        if (context.Estrategia?.FaseDados?.Temas.Count > 0)
            prompt += $"Temas: {string.Join(", ", context.Estrategia.FaseDados.Temas)}\n";
        prompt += $"\n## Mensagem original do cliente\n{context.MensagemOriginal}\n";
        return prompt;
    }

    internal static (bool? compativel, string? motivo) ParseCuradorResult(string resposta)
    {
        var json = JsonHelper.ExtrairJson(resposta);
        if (string.IsNullOrEmpty(json))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var compativel = root.TryGetProperty("compativel", out var c) ? c.GetBoolean() : (bool?)null;
            var motivo = root.TryGetProperty("motivo", out var m) ? m.GetString() : null;
            return (compativel, motivo);
        }
        catch
        {
            return (null, null);
        }
    }

    internal static string ConstruirDescricaoDefault(PipelineContext context)
    {
        var oferta = context.Brief.Oferta ?? context.Brief.Objetivo ?? "email marketing";
        var parts = new List<string> { $"Professional email marketing banner about {oferta}" };
        if (context.Estrategia?.Fase != null)
            parts.Add($"journey phase: {context.Estrategia.Fase}");
        if (context.Estrategia?.FaseDados?.Temas.Count > 0)
            parts.Add($"themes: {string.Join(", ", context.Estrategia.FaseDados.Temas)}");
        return string.Join(", ", parts);
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
        var logos = assets.Where(a => a.Tipo == TipoAsset.Logo).ToList();
        var icons = assets.Where(a => a.Tipo == TipoAsset.Icon).ToList();
        var banners = assets.Where(a => a.Tipo == TipoAsset.Banner).ToList();

        var descricoes = new List<string>();
        foreach (var asset in logos)
        {
            try
            {
                if (File.Exists(asset.Caminho))
                {
                    var bytes = await File.ReadAllBytesAsync(asset.Caminho, ct);
                    var descricaoAsset = await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
                    if (!string.IsNullOrEmpty(descricaoAsset))
                        descricoes.Add($"Logo: {descricaoAsset}");
                }
            }
            catch { }
        }

        foreach (var asset in icons)
        {
            try
            {
                if (File.Exists(asset.Caminho))
                {
                    var descricaoIcon = await _iconCache.ObterDescricaoAsync(asset, ct);
                    if (!string.IsNullOrEmpty(descricaoIcon.DescricaoGeral))
                        descricoes.Add($"Icon ({asset.Nome}): {descricaoIcon.ToPromptSection()}");
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
