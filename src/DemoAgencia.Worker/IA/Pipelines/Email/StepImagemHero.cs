using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class StepImagemHero : IPipelineStep
{
    public string Nome => "hero";

    private const string ModeloPrompt = "qwen/qwen3.7-plus";
    private const double TemperaturaPrompt = 0.7;
    private const int MaxTokensPrompt = 1000;

    private const string PersonaPrompt = """
        Voce e um especialista em direcao de arte para marketing. Sua funcao e criar prompts detalhados em ingles para geracao de imagens.

        ## Diretrizes
        - Escreva o prompt SEMPRE em ingles
        - Seja ultra-descritivo: cores, texturas, composicao, perspectiva
        - Priorize imagens limpas, profissionais, com foco no produto/sujeito
        - Use termos como: "high quality", "professional photography", "marketing material", "clean composition"
        - Para email marketing: composicao horizontal larga (landscape)

        ## Formato de Resposta
        Responda APENAS com o prompt em ingles, sem explicacoes adicionais, sem aspas, sem markdown.

        Exemplo de resposta:
        A professional marketing photograph of a modern workspace with a laptop and coffee cup on a wooden desk, soft natural lighting from the left, shallow depth of field, clean composition, high quality, corporate style
        """;

    private readonly IServicoChat _servicoChat;
    private readonly IGeradorImagem _geradorImagem;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;

    public StepImagemHero(
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem)
    {
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
    }

    public virtual async Task<PipelineContext> ExecutarAsync(PipelineContext context, CancellationToken ct)
    {
        var cliente = context.Cliente;
        var heroBrief = context.Brief.Imagens.FirstOrDefault(i =>
            i.Papel.Equals("hero", StringComparison.OrdinalIgnoreCase) ||
            i.Papel.Equals("banner", StringComparison.OrdinalIgnoreCase));

        if (heroBrief == null)
        {
            context.HeroSrc = null;
            return context;
        }

        var promptBase = await ConstruirPromptBase(heroBrief.Descricao, cliente, ct);
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

    private async Task<string> ConstruirPromptBase(string descricao, string? cliente, CancellationToken ct)
    {
        var prompt = descricao;

        if (string.IsNullOrEmpty(cliente))
            return prompt;

        var assets = _referencias.ListarAssets(cliente);
        var marcas = assets.Where(a => a.Tipo == TipoAsset.Logo || a.Tipo == TipoAsset.Icon).ToList();

        if (marcas.Count == 0)
            return prompt;

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
            catch
            {
                if (!string.IsNullOrEmpty(asset.Descricao))
                    descricoes.Add($"{asset.Tipo}: {asset.Descricao}");
            }
        }

        if (descricoes.Count > 0)
            prompt += "\n\nVisual identity references:\n" + string.Join("\n", descricoes);

        return prompt;
    }

    private async Task<string> GerarPromptDetalhado(long chatId, string promptBase, CancellationToken ct)
    {
        var resposta = await _servicoChat.ChamarAgenteAsync(
            chatId,
            PersonaPrompt,
            ModeloPrompt,
            promptBase,
            "email_hero_prompt",
            temperature: TemperaturaPrompt,
            maxTokens: MaxTokensPrompt,
            ct: ct);

        return resposta?.Trim() ?? promptBase;
    }
}
