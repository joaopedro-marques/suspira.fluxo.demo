using System.Net;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.Observabilidade;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DemoAgencia.Worker.IA;

public class OpenRouterService : IServicoChat, IGeradorImagem, IAnalisadorImagem
{
    private readonly ILogger<OpenRouterService> _logger;
    private readonly OpenRouterOptions _options;
    private readonly LangfuseInterceptor _langfuse;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenRouterService(
        ILogger<OpenRouterService> logger,
        IOptions<OpenRouterOptions> options,
        LangfuseInterceptor langfuse,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options.Value;
        _langfuse = langfuse;
        _httpClientFactory = httpClientFactory;
    }

    private Kernel CriarKernel(string modelo)
    {
        var httpClient = _httpClientFactory.CreateClient("OpenRouter");
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: string.IsNullOrEmpty(modelo) ? _options.DefaultModel : modelo,
            endpoint: new Uri(_options.BaseUrl),
            apiKey: _options.ApiKey,
            httpClient: httpClient);
        return builder.Build();
    }

    public virtual async Task<string> DescreverImagemAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default)
    {
        var modeloVisao = string.IsNullOrEmpty(_options.VisionModel) ? "qwen/qwen2.5-vl-72b-instruct" : _options.VisionModel;
        var traceContext = _langfuse.IniciarTrace(0, "image-analysis", modeloVisao);

        var kernel = CriarKernel(modeloVisao);
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage("Voce e um assistente que analisa imagens. Descreva o que ve na imagem de forma clara e concisa.");

        var prompt = string.IsNullOrEmpty(contexto)
            ? "Descreva esta imagem:"
            : $"Contexto: {contexto}\n\nDescreva esta imagem:";

        var base64Image = Convert.ToBase64String(imagemBytes);
        var dataUri = $"data:image/png;base64,{base64Image}";

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.5,
            MaxTokens = 1000
        };

        try
        {
            var chatMessage = new ChatMessageContent(
                AuthorRole.User,
                new ChatMessageContentItemCollection
                {
                    new ImageContent(dataUri),
                    new TextContent(prompt)
                });

            chatHistory.Add(chatMessage);

            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, kernel, ct);
            var resposta = response.Content ?? "";

            if (string.IsNullOrEmpty(resposta))
            {
                _logger.LogWarning("Analise de imagem retornou resposta vazia");
                await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[resposta vazia]", ct);
            }
            else
            {
                _logger.LogInformation("Imagem analisada com sucesso");
                await _langfuse.FinalizarTraceAsync(traceContext, prompt, resposta, ct);
            }

            return resposta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao analisar imagem");
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Erro: {ex.Message}", ct);
            throw;
        }
    }

    public virtual async Task<BannerDescricao> DescreverBannerAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default)
    {
        var modeloVisao = string.IsNullOrEmpty(_options.VisionModel) ? "qwen/qwen2.5-vl-72b-instruct" : _options.VisionModel;
        var traceContext = _langfuse.IniciarTrace(0, "banner-analysis", modeloVisao);

        var kernel = CriarKernel(modeloVisao);
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(BannerPromptSystema);

        var prompt = string.IsNullOrEmpty(contexto)
            ? BannerPromptUser
            : $"Contexto adicional: {contexto}\n\n{BannerPromptUser}";

        var base64Image = Convert.ToBase64String(imagemBytes);
        var dataUri = $"data:image/png;base64,{base64Image}";

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.4,
            MaxTokens = 1500
        };

        try
        {
            var chatMessage = new ChatMessageContent(
                AuthorRole.User,
                new ChatMessageContentItemCollection
                {
                    new ImageContent(dataUri),
                    new TextContent(prompt)
                });

            chatHistory.Add(chatMessage);

            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, kernel, ct);
            var resposta = response.Content ?? "";

            if (BannerDescricao.TryParse(resposta, out var descricao) && descricao != null)
            {
                _logger.LogInformation("Banner analisado com sucesso");
                await _langfuse.FinalizarTraceAsync(traceContext, prompt, resposta, ct);
                return descricao;
            }

            _logger.LogWarning("Falha ao parsear descricao do banner. Resposta: {Resposta}", resposta?[..Math.Min(200, resposta?.Length ?? 0)]);
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[parse failed]", ct);
            return new BannerDescricao();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao analisar banner");
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Erro: {ex.Message}", ct);
            return new BannerDescricao();
        }
    }

    public virtual async Task<IconDescricao> DescreverIconeAsync(
        byte[] imagemBytes,
        string? contexto,
        CancellationToken ct = default)
    {
        var modeloVisao = string.IsNullOrEmpty(_options.VisionModel) ? "qwen/qwen2.5-vl-72b-instruct" : _options.VisionModel;
        var traceContext = _langfuse.IniciarTrace(0, "icon-analysis", modeloVisao);

        var kernel = CriarKernel(modeloVisao);
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(IconPromptSystema);

        var prompt = string.IsNullOrEmpty(contexto)
            ? IconPromptUser
            : $"Contexto adicional: {contexto}\n\n{IconPromptUser}";

        var base64Image = Convert.ToBase64String(imagemBytes);
        var dataUri = $"data:image/png;base64,{base64Image}";

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3,
            MaxTokens = 1000
        };

        try
        {
            var chatMessage = new ChatMessageContent(
                AuthorRole.User,
                new ChatMessageContentItemCollection
                {
                    new ImageContent(dataUri),
                    new TextContent(prompt)
                });

            chatHistory.Add(chatMessage);

            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, kernel, ct);
            var resposta = response.Content ?? "";

            if (IconDescricao.TryParse(resposta, out var descricao) && descricao != null)
            {
                _logger.LogInformation("Icone analisado com sucesso");
                await _langfuse.FinalizarTraceAsync(traceContext, prompt, resposta, ct);
                return descricao;
            }

            _logger.LogWarning("Falha ao parsear descricao do icone. Resposta: {Resposta}", resposta?[..Math.Min(200, resposta?.Length ?? 0)]);
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[parse failed]", ct);
            return new IconDescricao();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao analisar icone");
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Erro: {ex.Message}", ct);
            return new IconDescricao();
        }
    }

    private const string IconPromptSystema = """
        Voce e um especialista em design visual e experiencia do usuario.
        Analise o icone/imagem fornecido e descreva o que ele representa de forma estruturada.
        Responda APENAS com JSON valido, sem explicacoes adicionais, sem markdown.

        Campos obrigatorios:
        {
            "descricao_geral": "Descricao do que o icone representa (1 frase concisa)",
            "palavras_chave": ["lista de 3-5 palavras-chave sobre quando usar este icone"],
            "estilo": "Estilo visual do icone (ex: flat line, filled, outline, duotone)"
        }
        """;

    private const string IconPromptUser = "Analise este icone e descreva o que ele representa e quando deve ser usado.";

    private const string BannerPromptSystema = """
        Voce e um especialista em direcao de arte e design visual para email marketing.
        Analise o banner/imagem fornecido e descreva suas caracteristicas visuais de forma estruturada.
        Responda APENAS com JSON valido, sem explicacoes adicionais, sem markdown.

        Campos obrigatorios:
        {
            "descricao_geral": "Descricao geral do que a imagem representa (1-2 frases)",
            "composicao": "Como os elementos estao organizados (layout, alinhamento, espaco)",
            "paleta_dominante": ["lista de cores dominantes em hex, ex: #006b40"],
            "estilo": "Estilo visual (ex: flat, fotográfico, ilustração, 3D, minimalista)",
            "mood": "Sensacao/ambiente que a imagem transmite (ex: profissional, acolhedor, urgente)",
            "texto_presente": "Texto visivel na imagem, se houver (ou string vazia)"
        }
        """;

    private const string BannerPromptUser = "Analise este banner e descreva suas caracteristicas visuais.";

    public virtual async Task<ResultadoImagem> GerarImagemAsync(
        long chatId,
        string prompt,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Gerando imagem ({Length} chars)", prompt.Length);

        var cadeia = MontarCadeiaModelos(_options.ImageModel, _options.ImageFallbackModels);

        for (var tentativa = 0; tentativa < cadeia.Length; tentativa++)
        {
            var modeloAtual = cadeia[tentativa];
            var traceContext = _langfuse.IniciarTrace(chatId, "image-generation", modeloAtual);

            var httpClient = _httpClientFactory.CreateClient("OpenRouter");
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

            var request = new
            {
                model = modeloAtual,
                prompt,
                n = 1
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            try
            {
                var response = await httpClient.PostAsync($"{_options.BaseUrl}/images", content, ct);
                var responseJson = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Erro 429 no modelo {modeloAtual}", ct);

                    if (tentativa < cadeia.Length - 1)
                    {
                        var delay = CalcularBackoff(tentativa);
                        _logger.LogWarning(
                            "429 na geracao de imagem com {Modelo}. Fallback para {Proximo} apos {Delay}s",
                            modeloAtual, cadeia[tentativa + 1], delay.TotalSeconds);
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    var erro429 = "HTTP 429 TooManyRequests";
                    _logger.LogError("429 em todos os modelos ({Total}) na geracao de imagem", cadeia.Length);
                    return new ResultadoImagem(null, erro429);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var erro = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
                    _logger.LogError("Erro ao gerar imagem: {Erro}", erro);
                    await _langfuse.FinalizarTraceAsync(traceContext, prompt, erro, ct);
                    return new ResultadoImagem(null, erro);
                }

                using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0)
                {
                    _logger.LogError("Resposta sem imagens");
                    await _langfuse.FinalizarTraceAsync(traceContext, prompt, "Resposta sem imagens", ct);
                    return new ResultadoImagem(null, "Resposta sem imagens");
                }

                var b64 = data[0].GetProperty("b64_json").GetString();
                if (string.IsNullOrEmpty(b64))
                {
                    _logger.LogError("b64_json vazio");
                    await _langfuse.FinalizarTraceAsync(traceContext, prompt, "b64_json vazio", ct);
                    return new ResultadoImagem(null, "b64_json vazio");
                }

                _logger.LogInformation("Imagem gerada com sucesso ({Modelo})", modeloAtual);

                await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[imagem gerada]", ct);

                return new ResultadoImagem(Convert.FromBase64String(b64), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar imagem ({Modelo})", modeloAtual);
                await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Excecao: {ex.Message}", ct);
                return new ResultadoImagem(null, $"Excecao: {ex.Message}");
            }
        }

        return new ResultadoImagem(null, "Cadeia de modelos vazia");
    }

    public virtual async Task<string> ChamarAgenteAsync(
        long chatId,
        string persona,
        string modelo,
        string instrucoes,
        string etapaNome = "pipeline-step",
        double temperature = 0.7,
        int maxTokens = 2000,
        CancellationToken ct = default)
    {
        var cadeia = MontarCadeiaModelos(modelo, _options.FallbackModels);

        for (var tentativa = 0; tentativa < cadeia.Length; tentativa++)
        {
            var modeloAtual = cadeia[tentativa];
            var traceContext = _langfuse.IniciarTrace(chatId, etapaNome, modeloAtual);

            try
            {
                var desativarRaciocinio = etapaNome.StartsWith("router", StringComparison.OrdinalIgnoreCase);
                ReasoningDisablingHandler.IsActive = desativarRaciocinio;
                var kernel = CriarKernel(modeloAtual);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();

                var chatHistory = new ChatHistory();

                if (!string.IsNullOrEmpty(persona))
                {
                    chatHistory.AddSystemMessage(persona);
                }

                chatHistory.AddUserMessage(instrucoes);

                var settings = new OpenAIPromptExecutionSettings
                {
                    Temperature = temperature,
                    MaxTokens = maxTokens
                };

                var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, kernel, ct);
                var resposta = response.Content ?? "";

                if (string.IsNullOrEmpty(resposta))
                {
                    var finishReason = response.Metadata?.TryGetValue("FinishReason", out var fr) == true ? fr?.ToString() : "unknown";
                    _logger.LogWarning("Agente ({Etapa}) com {Modelo} retornou resposta vazia. FinishReason: {FinishReason}",
                        etapaNome, modeloAtual, finishReason);
                }
                else
                {
                    _logger.LogInformation("Agente chamado ({Etapa}) com {Modelo}: {Length} chars", etapaNome, modeloAtual, resposta.Length);
                }

                await _langfuse.FinalizarTraceAsync(traceContext, instrucoes, resposta, ct);

                return resposta;
            }
            catch (Exception ex) when (Eh429(ex))
            {
                await _langfuse.FinalizarTraceAsync(traceContext, instrucoes, $"Erro 429 no modelo {modeloAtual}", ct);

                if (tentativa < cadeia.Length - 1)
                {
                    var delay = CalcularBackoff(tentativa);
                    _logger.LogWarning(
                        "429 no modelo {Modelo} ({Etapa}). Fallback para {Proximo} apos {Delay}s",
                        modeloAtual, etapaNome, cadeia[tentativa + 1], delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
                else
                {
                    _logger.LogError(ex, "429 em todos os modelos ({Total}) na etapa {Etapa}", cadeia.Length, etapaNome);
                    throw;
                }
            }
            catch (Exception ex)
            {
                await _langfuse.FinalizarTraceAsync(traceContext, instrucoes, $"Erro: {ex.Message}", ct);
                _logger.LogError(ex, "Erro ao chamar agente ({Etapa}) com modelo {Modelo}", etapaNome, modeloAtual);
                throw;
            }
        }

        throw new InvalidOperationException("Cadeia de modelos vazia");
    }

    private static bool Eh429(Exception ex)
    {
        if (ex is HttpOperationException httpEx)
            return httpEx.StatusCode == HttpStatusCode.TooManyRequests;

        if (ex.InnerException is HttpOperationException innerHttpEx)
            return innerHttpEx.StatusCode == HttpStatusCode.TooManyRequests;

        return false;
    }

    private static string[] MontarCadeiaModelos(string principal, string[] fallbacks)
    {
        var cadeia = new List<string> { principal };
        foreach (var m in fallbacks)
        {
            if (!string.IsNullOrWhiteSpace(m) && !cadeia.Contains(m))
                cadeia.Add(m);
        }
        return cadeia.ToArray();
    }

    private TimeSpan CalcularBackoff(int tentativa)
    {
        var baseSegundos = Math.Max(1, _options.Backoff429BaseSegundos);
        var delay = baseSegundos * Math.Pow(2, tentativa);
        var cap = Math.Max(1, _options.Backoff429MaxSegundos);
        return TimeSpan.FromSeconds(Math.Min(delay, cap));
    }
}

internal class ReasoningDisablingHandler : DelegatingHandler
{
    private static readonly System.Threading.AsyncLocal<bool> Active = new();

    public static bool IsActive { get => Active.Value; set => Active.Value = value; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Active.Value && request.Content != null && request.RequestUri?.AbsolutePath.Contains("/chat/completions") == true)
        {
            try
            {
                var originalBody = await request.Content.ReadAsStringAsync(cancellationToken);
                if (System.Text.Json.Nodes.JsonNode.Parse(originalBody) is System.Text.Json.Nodes.JsonObject obj)
                {
                    obj["reasoning"] = new System.Text.Json.Nodes.JsonObject { ["enabled"] = false };
                    request.Content = new StringContent(obj.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
                }
            }
            catch { }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
