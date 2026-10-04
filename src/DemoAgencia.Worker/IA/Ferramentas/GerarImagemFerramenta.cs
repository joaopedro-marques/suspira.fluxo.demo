using System.Text.Json;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Ferramentas;

public class GerarImagemFerramenta : IFerramenta
{
    private readonly ILogger<GerarImagemFerramenta> _logger;
    private readonly IGeradorImagem _openRouter;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly LoopOptions _options;

    public string Nome => "gerar_imagem";
    public string Descricao => "Gera uma imagem a partir de um prompt textual, com papel e plano de deck. Use substituir para refazer uma imagem existente na mesma posicao.";

    public GerarImagemFerramenta(ILogger<GerarImagemFerramenta> logger, IGeradorImagem openRouter, IReferenciasCliente referencias, IAnalisadorImagem analisadorImagem, LoopOptions options)
    {
        _logger = logger;
        _openRouter = openRouter;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _options = options;
    }

    public async Task<string> ExecutarAsync(LoopContext context, JsonElement parametros, CancellationToken ct)
    {
        var prompt = parametros.TryGetProperty("prompt", out var promptEl)
            ? promptEl.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Falha: prompt vazio";
        }

        var legenda = parametros.TryGetProperty("legenda", out var legEl)
            ? legEl.GetString()
            : null;

        var papel = parametros.TryGetProperty("papel", out var papelEl)
            ? papelEl.GetString()
            : null;

        var substituir = parametros.TryGetProperty("substituir", out var substEl)
            ? substEl.GetString()
            : null;

        if (!string.IsNullOrEmpty(substituir))
        {
            var indiceExistente = context.ImagensDeck.FindIndex(i => i.Id == substituir);
            if (indiceExistente < 0)
            {
                return $"Falha: {substituir} nao existe no deck. Imagens disponiveis: [{string.Join(", ", context.ImagensDeck.Select(i => i.Id))}].";
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(papel) && context.ImagensDeck.Any(i => i.Papel == papel))
            {
                var existente = context.ImagensDeck.First(i => i.Papel == papel);
                return $"Falha: papel '{papel}' ja existe como {existente.Id} no deck. Use substituir: '{existente.Id}' para refazer, ou use um papel diferente.";
            }

            if (context.PlanoDeck.Count > 0 && !string.IsNullOrEmpty(papel) && !context.PlanoDeck.Contains(papel))
            {
                return $"Falha: papel '{papel}' nao esta no plano [{string.Join(", ", context.PlanoDeck)}]. Revise com planejar_deck.";
            }

            if (context.ImagensDeck.Count >= _options.MaxImagensPorDeck)
            {
                return $"Falha: limite de {_options.MaxImagensPorDeck} imagens no deck atingido. Use substituir para refazer ou planejar_deck para redefinir.";
            }
        }

        var promptEnriquecido = await EnriquecerComAssetsAsync(prompt, parametros, context, ct);

        _logger.LogInformation("Gerando imagem com prompt: {Prompt}", promptEnriquecido);

        var resultado = await _openRouter.GerarImagemAsync(context.ChatId, promptEnriquecido, ct);

        if (resultado.Sucesso)
        {
            var promptResumo = prompt.Length > 80 ? prompt[..80] + "..." : prompt;

            if (!string.IsNullOrEmpty(substituir))
            {
                var indice = context.ImagensDeck.FindIndex(i => i.Id == substituir);
                var itemDeck = context.ImagensDeck[indice];
                var novoPapel = papel ?? itemDeck.Papel;
                var novoItem = new ItemDeckImagem(itemDeck.Id, novoPapel, legenda, promptResumo);
                context.SubstituirImagemNoDeck(indice, novoItem);
                context.Resultado.Imagens[indice] = new ImagemGerada(resultado.Bytes!, legenda);

                var papeis = string.Join(", ", context.ImagensDeck.Select(i => i.Papel));
                return $"Imagem {itemDeck.Id} ({novoPapel}) substituida com sucesso. Deck: {context.ImagensDeck.Count} imagens [{papeis}].";
            }

            var id = $"img_{context.ImagensDeck.Count + 1}";
            var papelFinal = papel ?? id;

            context.Resultado.Imagens.Add(new ImagemGerada(resultado.Bytes!, legenda));
            context.AdicionarImagemNoDeck(new ItemDeckImagem(id, papelFinal, legenda, promptResumo));

            var papeisLista = string.Join(", ", context.ImagensDeck.Select(i => i.Papel));
            return $"Imagem {id} ({papelFinal}) gerada com sucesso. Deck: {context.ImagensDeck.Count} imagens [{papeisLista}].";
        }

        return $"Falha ao gerar imagem: {resultado.Erro}";
    }

    private async Task<string> EnriquecerComAssetsAsync(string prompt, JsonElement parametros, LoopContext context, CancellationToken ct)
    {
        if (!parametros.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            return prompt;

        var cliente = context.Cliente;
        if (string.IsNullOrEmpty(cliente))
            return prompt;

        var assetsDoCliente = _referencias.ListarAssets(cliente);
        var descricoes = new List<string>();

        foreach (var item in assetsEl.EnumerateArray())
        {
            var id = item.GetString();
            if (string.IsNullOrEmpty(id)) continue;

            var asset = assetsDoCliente.FirstOrDefault(a => a.Id == id);
            if (asset == null) continue;

            try
            {
                var bytes = await File.ReadAllBytesAsync(asset.Caminho, ct);
                var descricao = await _analisadorImagem.DescreverImagemAsync(bytes, null, ct);
                if (!string.IsNullOrEmpty(descricao))
                    descricoes.Add($"{asset.Tipo}/{asset.Nome}: {descricao}");
            }
            catch
            {
                if (!string.IsNullOrEmpty(asset.Descricao))
                    descricoes.Add($"{asset.Tipo}/{asset.Nome}: {asset.Descricao}");
            }
        }

        if (descricoes.Count == 0)
            return prompt;

        return prompt + "\n\nVisual identity references:\n" + string.Join("\n", descricoes);
    }
}
