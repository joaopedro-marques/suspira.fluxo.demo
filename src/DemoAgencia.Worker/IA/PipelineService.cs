using System.Text.Json;
using DemoAgencia.Worker.Agentes;

namespace DemoAgencia.Worker.IA;

public class PipelineService
{
    private readonly ILogger<PipelineService> _logger;
    private readonly OpenRouterService _openRouter;
    private readonly AgenteLoader _agenteLoader;
    private readonly HistoricoChat _historico;
    private readonly IConfiguration _configuration;

    public PipelineService(
        ILogger<PipelineService> logger,
        OpenRouterService openRouter,
        AgenteLoader agenteLoader,
        HistoricoChat historico,
        IConfiguration configuration)
    {
        _logger = logger;
        _openRouter = openRouter;
        _agenteLoader = agenteLoader;
        _historico = historico;
        _configuration = configuration;
    }

    public virtual async Task<ResultadoPipeline> ExecutarAsync(
        long chatId,
        string mensagem,
        Func<string, Task>? onProgresso = null,
        CancellationToken ct = default)
    {
        var resultado = new ResultadoPipeline();
        var maxRefacoes = _configuration.GetValue<int>("Pipeline:MaxRefacoes", 2);

        // 1. ORQUESTRADOR
        await NotificarProgresso(onProgresso, "🧠 Analisando seu pedido...");
        resultado.EtapasExecutadas.Add("orquestrador");

        var orquestrador = _agenteLoader.ObterPorPapel("orquestrador");
        if (orquestrador == null)
        {
            _logger.LogError("Agente orquestrador nao encontrado");
            resultado.RespostaFinal = "Erro interno: orquestrador nao configurado.";
            return resultado;
        }

        var historicoMensagens = _historico.ObterHistorico(chatId);
        var historicoTexto = string.Join("\n", historicoMensagens.Select(m => $"{m.Role}: {m.Content}"));
        var agentesProducao = _agenteLoader.ListarAgentesProducao();
        var listaAgentes = string.Join(", ", agentesProducao.Select(a => $"{a.Nome}: {a.Descricao}"));

        var instrucoesOrquestrador = $"Historico do chat:\n{historicoTexto}\n\nMensagem atual: {mensagem}\n\nAgentes de producao disponiveis: {listaAgentes}";

        var respostaOrquestrador = await _openRouter.ChamarAgenteAsync(
            orquestrador.Persona,
            orquestrador.ModeloAlvo,
            instrucoesOrquestrador,
            "orquestrador",
            temperature: 0.3,
            ct: ct);

        var jsonOrquestrador = OpenRouterService.ExtrairJson(respostaOrquestrador);
        string acao = "fora_contexto";
        string? briefing = null;
        string? respostaDireta = null;

        if (string.IsNullOrEmpty(jsonOrquestrador))
        {
            // No JSON found, fallback to direta with raw response
            _logger.LogWarning("Orquestrador nao retornou JSON valido, usando fallback para direta");
            acao = "direta";
            respostaDireta = respostaOrquestrador;
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonOrquestrador);
                acao = doc.RootElement.GetProperty("acao").GetString() ?? "fora_contexto";
                
                if (doc.RootElement.TryGetProperty("briefing", out var briefingEl))
                    briefing = briefingEl.GetString();
                if (doc.RootElement.TryGetProperty("resposta", out var respostaEl))
                    respostaDireta = respostaEl.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao parsear JSON do orquestrador, usando fallback");
                acao = "direta";
                respostaDireta = respostaOrquestrador;
            }
        }

        resultado.Rota = acao;

        // ROTA: FORA_CONTEXTO
        if (acao == "fora_contexto")
        {
            resultado.RespostaFinal = _configuration["Pipeline:MensagemForaContexto"] 
                ?? "Opa, *suspiro*, infelizmente nao consigo te responder sobre isso.";
            return resultado;
        }

        // ROTA: DIRETA
        if (acao == "direta")
        {
            await NotificarProgresso(onProgresso, "📤 Formatando resposta...");
            resultado.EtapasExecutadas.Add("formatador");

            var formatador = _agenteLoader.ObterPorPapel("formatacao");
            if (formatador != null && !string.IsNullOrEmpty(respostaDireta))
            {
                var respostaFormatada = await _openRouter.ChamarAgenteAsync(
                    formatador.Persona,
                    formatador.ModeloAlvo,
                    $"Pedido original: {mensagem}\n\nResposta para formatar:\n{respostaDireta}",
                    "formatador",
                    ct: ct);

                resultado.RespostaFinal = respostaFormatada;
            }
            else
            {
                resultado.RespostaFinal = respostaDireta ?? respostaOrquestrador;
            }

            _historico.AdicionarMensagem(chatId, "user", mensagem);
            _historico.AdicionarMensagem(chatId, "assistant", resultado.RespostaFinal);
            return resultado;
        }

        // ROTA: PIPELINE
        if (string.IsNullOrEmpty(briefing))
        {
            _logger.LogWarning("Briefing vazio do orquestrador, usando mensagem original");
            briefing = mensagem;
        }

        var refacoes = 0;
        string? outputProducao = null;
        string? instrucoesProducao = null;
        AgenteDefinicao? agenteProducao = null;

        while (refacoes <= maxRefacoes)
        {
            // 2. ESTRATEGISTA (PLANEJADOR) - so na primeira iteracao
            if (refacoes == 0)
            {
                await NotificarProgresso(onProgresso, "📋 Planejando execucao...");
                resultado.EtapasExecutadas.Add("estrategista_planejador");

                var estrategista = _agenteLoader.ObterPorPapel("estrategista");
                if (estrategista == null)
                {
                    _logger.LogError("Agente estrategista nao encontrado");
                    resultado.RespostaFinal = "Erro interno: estrategista nao configurado.";
                    return resultado;
                }

                var planoEstrategista = await _openRouter.ChamarAgenteAsync(
                    estrategista.Persona,
                    estrategista.ModeloAlvo,
                    $"Briefing do orquestrador:\n{briefing}\n\nAgentes disponiveis: {listaAgentes}",
                    "estrategista_planejador",
                    temperature: 0.3,
                    ct: ct);

                var jsonPlano = OpenRouterService.ExtrairJson(planoEstrategista);
                if (!string.IsNullOrEmpty(jsonPlano))
                {
                    try
                    {
                        using var docPlano = JsonDocument.Parse(jsonPlano);
                        var nomeAgente = docPlano.RootElement.GetProperty("agente").GetString();
                        instrucoesProducao = docPlano.RootElement.GetProperty("instrucoes").GetString();

                        agenteProducao = _agenteLoader.ObterPorNome(nomeAgente ?? "");
                        if (agenteProducao == null)
                        {
                            _logger.LogWarning("Agente {Agente} nao encontrado, usando primeiro de producao", nomeAgente);
                            agenteProducao = agentesProducao.FirstOrDefault();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erro ao parsear plano do estrategista");
                        agenteProducao = agentesProducao.FirstOrDefault();
                        instrucoesProducao = briefing;
                    }
                }
                else
                {
                    agenteProducao = agentesProducao.FirstOrDefault();
                    instrucoesProducao = briefing;
                }
            }

            if (agenteProducao == null || string.IsNullOrEmpty(instrucoesProducao))
            {
                _logger.LogError("Agente de producao ou instrucoes vazias");
                resultado.RespostaFinal = _configuration["Pipeline:MensagemFalhaPipeline"] 
                    ?? "Nao consegui produzir um resultado. Tente reformular.";
                return resultado;
            }

            // 3. PRODUCAO
            await NotificarProgresso(onProgresso, $"✍️ Produzindo com {agenteProducao.Nome}...");
            resultado.EtapasExecutadas.Add($"producao_{agenteProducao.Nome}");

            var isEditorImagens = agenteProducao.Nome.Contains("Editor de Imagens", StringComparison.OrdinalIgnoreCase)
                || agenteProducao.Nome.Contains("EditorImagens", StringComparison.OrdinalIgnoreCase);

            if (isEditorImagens)
            {
                // Fluxo especial para editor de imagens:
                // 1. Enriquecer prompt (editor agent → optimized prompt)
                // 2. Gerar imagem (optimized prompt → image bytes)
                // 3. QA revisa o prompt otimizado (texto)
                // 4. Output para aprovador = descricao da imagem gerada

                var promptOtimizado = await _openRouter.ChamarAgenteAsync(
                    agenteProducao.Persona,
                    agenteProducao.ModeloAlvo,
                    instrucoesProducao,
                    $"producao_{agenteProducao.Nome}_enriquecimento",
                    ct: ct);

                await NotificarProgresso(onProgresso, "🎨 Gerando imagem...");
                var imagemBytes = await _openRouter.GerarImagemAsync(promptOtimizado, ct);

                if (imagemBytes != null)
                {
                    resultado.Imagem = imagemBytes;
                    resultado.LegendaImagem = mensagem; // pedido original como caption
                    outputProducao = $"Imagem gerada com sucesso. Prompt otimizado: {promptOtimizado}";
                }
                else
                {
                    outputProducao = "Falha ao gerar imagem.";
                }
            }
            else
            {
                outputProducao = await _openRouter.ChamarAgenteAsync(
                    agenteProducao.Persona,
                    agenteProducao.ModeloAlvo,
                    instrucoesProducao,
                    $"producao_{agenteProducao.Nome}",
                    ct: ct);
            }

            // 4. QUALIDADE
            await NotificarProgresso(onProgresso, "🔍 Revisando qualidade...");
            resultado.EtapasExecutadas.Add("qualidade");

            var qualidade = _agenteLoader.ObterPorPapel("qualidade");
            if (qualidade == null)
            {
                _logger.LogError("Agente qualidade nao encontrado");
                resultado.RespostaFinal = "Erro interno: qualidade nao configurada.";
                return resultado;
            }

            var instrucoesQualidade = $"Instrucoes originais:\n{instrucoesProducao}\n\nOutput do agente:\n{outputProducao}";
            if (refacoes > 0)
            {
                instrucoesQualidade += $"\n\nFeedback da iteracao anterior: {instrucoesProducao}";
            }

            var vereditoQualidade = await _openRouter.ChamarAgenteAsync(
                qualidade.Persona,
                qualidade.ModeloAlvo,
                instrucoesQualidade,
                "qualidade",
                temperature: 0.3,
                ct: ct);

            var jsonVeredito = OpenRouterService.ExtrairJson(vereditoQualidade);
            string veredito = "aprovado";
            string? feedback = null;

            if (!string.IsNullOrEmpty(jsonVeredito))
            {
                try
                {
                    using var docVeredito = JsonDocument.Parse(jsonVeredito);
                    veredito = docVeredito.RootElement.GetProperty("veredito").GetString() ?? "aprovado";
                    if (docVeredito.RootElement.TryGetProperty("feedback", out var feedbackEl))
                        feedback = feedbackEl.GetString();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro ao parsear veredito da qualidade, assumindo aprovado");
                    veredito = "aprovado";
                }
            }

            // Se reprovado e ainda tem refacoes, volta para producao
            if (veredito == "reprovado" && refacoes < maxRefacoes)
            {
                refacoes++;
                _logger.LogInformation("Qualidade reprovou, refacao {Refacao}/{Max}", refacoes, maxRefacoes);
                instrucoesProducao = $"Instrucoes originais:\n{instrucoesProducao}\n\nFeedback para correcao:\n{feedback}";
                await NotificarProgresso(onProgresso, $"🔁 Refinando ({refacoes}/{maxRefacoes})...");
                continue;
            }

            // 5. ESTRATEGISTA (APROVADOR)
            await NotificarProgresso(onProgresso, "✅ Aprovando...");
            resultado.EtapasExecutadas.Add("estrategista_aprovador");

            var estrategistaAprovador = _agenteLoader.ObterPorPapel("estrategista");
            if (estrategistaAprovador == null)
            {
                _logger.LogError("Agente estrategista nao encontrado para aprovacao");
                resultado.RespostaFinal = "Erro interno: estrategista nao configurado.";
                return resultado;
            }

            var instrucoesAprovacao = $"Briefing original:\n{briefing}\n\nOutput do agente:\n{outputProducao}\n\nVeredito da qualidade: {veredito}\nFeedback: {feedback}";

            var aprovacaoEstrategista = await _openRouter.ChamarAgenteAsync(
                estrategistaAprovador.Persona,
                estrategistaAprovador.ModeloAlvo,
                instrucoesAprovacao,
                "estrategista_aprovador",
                temperature: 0.3,
                ct: ct);

            var jsonAprovacao = OpenRouterService.ExtrairJson(aprovacaoEstrategista);
            bool aprovado = true;
            string? observacoes = null;

            if (!string.IsNullOrEmpty(jsonAprovacao))
            {
                try
                {
                    using var docAprovacao = JsonDocument.Parse(jsonAprovacao);
                    aprovado = docAprovacao.RootElement.GetProperty("aprovado").GetBoolean();
                    if (docAprovacao.RootElement.TryGetProperty("observacoes", out var obsEl))
                        observacoes = obsEl.GetString();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro ao parsear aprovacao do estrategista, assumindo aprovado");
                    aprovado = true;
                }
            }

            // Se reprovado e ainda tem refacoes, volta para producao
            if (!aprovado && refacoes < maxRefacoes)
            {
                refacoes++;
                _logger.LogInformation("Estrategista reprovou, refacao {Refacao}/{Max}", refacoes, maxRefacoes);
                instrucoesProducao = $"Instrucoes originais:\n{instrucoesProducao}\n\nFeedback para correcao:\n{observacoes}";
                await NotificarProgresso(onProgresso, $"🔁 Refinando ({refacoes}/{maxRefacoes})...");
                continue;
            }

            // Aprovado, sai do loop
            break;
        }

        // Se saiu do loop sem aprovar
        if (refacoes >= maxRefacoes)
        {
            _logger.LogWarning("Pipeline excedeu maximo de refacoes");
            resultado.RespostaFinal = _configuration["Pipeline:MensagemFalhaPipeline"] 
                ?? "Nao consegui produzir um resultado com a qualidade esperada. Pode reformular o pedido?";
            return resultado;
        }

        // 6. FORMATADOR
        await NotificarProgresso(onProgresso, "📤 Formatando resposta final...");
        resultado.EtapasExecutadas.Add("formatador");

        var formatadorFinal = _agenteLoader.ObterPorPapel("formatacao");
        if (formatadorFinal != null && !string.IsNullOrEmpty(outputProducao))
        {
            var respostaFormatada = await _openRouter.ChamarAgenteAsync(
                formatadorFinal.Persona,
                formatadorFinal.ModeloAlvo,
                $"Pedido original do usuario: {mensagem}\n\nOutput aprovado para formatar:\n{outputProducao}",
                "formatador",
                ct: ct);

            resultado.RespostaFinal = respostaFormatada;
        }
        else
        {
            resultado.RespostaFinal = outputProducao ?? "";
        }

        // Salvar no historico
        _historico.AdicionarMensagem(chatId, "user", mensagem);
        _historico.AdicionarMensagem(chatId, "assistant", resultado.RespostaFinal);

        return resultado;
    }

    private static async Task NotificarProgresso(Func<string, Task>? onProgresso, string mensagem)
    {
        if (onProgresso != null)
        {
            await onProgresso(mensagem);
        }
    }
}
