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
        var modeloVisao = "qwen/qwen2.5-vl-72b-instruct";
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

    public virtual async Task<ResultadoImagem> GerarImagemAsync(
        long chatId,
        string prompt,
        CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(chatId, "image-generation", _options.ImageModel);

        _logger.LogInformation("Gerando imagem ({Length} chars)", prompt.Length);

        var httpClient = _httpClientFactory.CreateClient("OpenRouter");
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var request = new
        {
            model = _options.ImageModel,
            prompt = prompt,
            n = 1
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync($"{_options.BaseUrl}/images", content, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

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

            _logger.LogInformation("Imagem gerada com sucesso");

            await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[imagem gerada]", ct);

            return new ResultadoImagem(Convert.FromBase64String(b64), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar imagem");
            await _langfuse.FinalizarTraceAsync(traceContext, prompt, $"Excecao: {ex.Message}", ct);
            return new ResultadoImagem(null, $"Excecao: {ex.Message}");
        }
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
        var traceContext = _langfuse.IniciarTrace(chatId, etapaNome, modelo);

        try
        {
            var desativarRaciocinio = etapaNome.StartsWith("router", StringComparison.OrdinalIgnoreCase);
            ReasoningDisablingHandler.IsActive = desativarRaciocinio;
            var kernel = CriarKernel(modelo);
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
                    etapaNome, modelo, finishReason);
            }
            else
            {
                _logger.LogInformation("Agente chamado ({Etapa}) com {Modelo}: {Length} chars", etapaNome, modelo, resposta.Length);
            }

            await _langfuse.FinalizarTraceAsync(traceContext, instrucoes, resposta, ct);

            return resposta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar agente ({Etapa}) com modelo {Modelo}", etapaNome, modelo);
            throw;
        }
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
