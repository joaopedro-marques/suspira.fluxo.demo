using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Observabilidade;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DemoAgencia.Worker.IA;

public class OpenRouterService
{
    private readonly ILogger<OpenRouterService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Kernel _kernel;
    private readonly LangfuseInterceptor _langfuse;

    public OpenRouterService(
        ILogger<OpenRouterService> logger,
        IConfiguration configuration,
        LangfuseInterceptor langfuse)
    {
        _logger = logger;
        _configuration = configuration;
        _langfuse = langfuse;

        var apiKey = _configuration["OpenRouter:ApiKey"] ?? "";
        var baseUrl = _configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: "openai/auto",
            endpoint: new Uri(baseUrl),
            apiKey: apiKey);

        _kernel = builder.Build();
    }

    public virtual async Task<string> CompletarAsync(
        long chatId,
        string mensagem,
        string? persona,
        string modelo,
        HistoricoChat historico,
        CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(chatId, "chat-completion", modelo);

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();

        if (!string.IsNullOrEmpty(persona))
        {
            chatHistory.AddSystemMessage(persona);
        }

        var historicoMensagens = historico.ObterHistorico(chatId);
        foreach (var msg in historicoMensagens)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        chatHistory.AddUserMessage(mensagem);

        try
        {
            var settings = new OpenAIPromptExecutionSettings
            {
                ModelId = modelo,
                Temperature = 0.7,
                MaxTokens = 2000
            };

            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, _kernel, ct);
            var resposta = response.Content ?? "";

            historico.AdicionarMensagem(chatId, "user", mensagem);
            historico.AdicionarMensagem(chatId, "assistant", resposta);

            _logger.LogInformation("Completado com {Modelo}", modelo);

            await _langfuse.FinalizarTraceAsync(traceContext, mensagem, resposta, ct);

            return resposta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao completar com modelo {Modelo}", modelo);
            throw;
        }
    }

    public async IAsyncEnumerable<string> CompletarStreamingAsync(
        long chatId,
        string mensagem,
        string? persona,
        string modelo,
        HistoricoChat historico,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(chatId, "chat-completion-streaming", modelo);

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();

        if (!string.IsNullOrEmpty(persona))
        {
            chatHistory.AddSystemMessage(persona);
        }

        var historicoMensagens = historico.ObterHistorico(chatId);
        foreach (var msg in historicoMensagens)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        chatHistory.AddUserMessage(mensagem);

        var settings = new OpenAIPromptExecutionSettings
        {
            ModelId = modelo,
            Temperature = 0.7,
            MaxTokens = 2000
        };

        var respostaCompleta = "";
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, settings, _kernel, ct))
        {
            var content = chunk.Content;
            if (!string.IsNullOrEmpty(content))
            {
                respostaCompleta += content;
                yield return content;
            }
        }

        historico.AdicionarMensagem(chatId, "user", mensagem);
        historico.AdicionarMensagem(chatId, "assistant", respostaCompleta);

        _logger.LogInformation("Streaming completo com {Modelo}: {Length} caracteres", modelo, respostaCompleta.Length);

        await _langfuse.FinalizarTraceAsync(traceContext, mensagem, respostaCompleta, ct);
    }

    public virtual async Task<string> AnalisarImagemAsync(
        long chatId,
        byte[] imagemBytes,
        string? contexto,
        HistoricoChat historico,
        CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(chatId, "image-analysis", "google/gemini-flash-1.5");

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage("Voce e um assistente que analisa imagens. Descreva o que ve na imagem de forma clara e concisa.");

        var historicoMensagens = historico.ObterHistorico(chatId);
        foreach (var msg in historicoMensagens)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        var prompt = string.IsNullOrEmpty(contexto) 
            ? "Descreva esta imagem:" 
            : $"Contexto: {contexto}\n\nDescreva esta imagem:";

        var base64Image = Convert.ToBase64String(imagemBytes);
        var dataUri = $"data:image/png;base64,{base64Image}";
        
        var chatMessage = new ChatMessageContent(
            AuthorRole.User,
            new ChatMessageContentItemCollection
            {
                new ImageContent(new Uri(dataUri)),
                new TextContent(prompt)
            });

        chatHistory.Add(chatMessage);

        var settings = new OpenAIPromptExecutionSettings
        {
            ModelId = "google/gemini-flash-1.5",
            Temperature = 0.5,
            MaxTokens = 1000
        };

        try
        {
            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, _kernel, ct);
            var resposta = response.Content ?? "";

            historico.AdicionarMensagem(chatId, "user", contexto ?? "[imagem]");
            historico.AdicionarMensagem(chatId, "assistant", resposta);

            _logger.LogInformation("Imagem analisada com sucesso");

            await _langfuse.FinalizarTraceAsync(traceContext, contexto ?? "[imagem]", resposta, ct);

            return resposta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao analisar imagem");
            throw;
        }
    }

    public virtual async Task<byte[]?> GerarImagemAsync(
        string prompt,
        CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(0, "image-generation", "openai/gpt-image-1");

        _logger.LogInformation("Gerando imagem com prompt: {Prompt}", prompt.Substring(0, Math.Min(50, prompt.Length)));

        var httpClient = new HttpClient();
        var apiKey = _configuration["OpenRouter:ApiKey"] ?? "";

        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://demoagencia.local");

        var request = new
        {
            model = "openai/gpt-image-1",
            prompt = prompt,
            n = 1,
            size = "1024x1024",
            response_format = "b64_json"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync("https://openrouter.ai/api/v1/images/generations", content, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Erro ao gerar imagem: {StatusCode} - {Response}", response.StatusCode, responseJson);
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
            {
                _logger.LogError("Resposta sem imagens");
                return null;
            }

            var b64 = data[0].GetProperty("b64_json").GetString();
            if (string.IsNullOrEmpty(b64))
            {
                _logger.LogError("b64_json vazio");
                return null;
            }

            _logger.LogInformation("Imagem gerada com sucesso");

            await _langfuse.FinalizarTraceAsync(traceContext, prompt, "[imagem gerada]", ct);

            return Convert.FromBase64String(b64);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar imagem");
            return null;
        }
    }

    public virtual async Task<string> ClassificarAsync(string mensagem, CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(0, "classification", "google/gemini-flash-1.5");

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var prompt = $@"Classifique a seguinte mensagem em UMA das categorias: 'codigo', 'estrategia', 'copy', 'geral', 'image'.

Mensagem: {mensagem}

Responda APENAS com a categoria (codigo, estrategia, copy, geral ou image):";

        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(prompt);

        var settings = new OpenAIPromptExecutionSettings
        {
            ModelId = "google/gemini-flash-1.5",
            Temperature = 0.1,
            MaxTokens = 10
        };

        try
        {
            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, _kernel, ct);
            var categoria = (response.Content ?? "geral").Trim().ToLowerInvariant();

            _logger.LogInformation("Classificação: {Mensagem} -> {Categoria}", mensagem.Substring(0, Math.Min(50, mensagem.Length)), categoria);

            await _langfuse.FinalizarTraceAsync(traceContext, mensagem, categoria, ct);

            return categoria;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao classificar mensagem");
            return "geral";
        }
    }

    public virtual async Task<string> ChamarAgenteAsync(
        string persona,
        string modelo,
        string instrucoes,
        string etapaNome = "pipeline-step",
        double temperature = 0.7,
        int maxTokens = 2000,
        CancellationToken ct = default)
    {
        var traceContext = _langfuse.IniciarTrace(0, etapaNome, modelo);

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory();

        if (!string.IsNullOrEmpty(persona))
        {
            chatHistory.AddSystemMessage(persona);
        }

        chatHistory.AddUserMessage(instrucoes);

        try
        {
            var settings = new OpenAIPromptExecutionSettings
            {
                ModelId = modelo,
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            var response = await chatService.GetChatMessageContentAsync(chatHistory, settings, _kernel, ct);
            var resposta = response.Content ?? "";

            _logger.LogInformation("Agente chamado ({Etapa}) com {Modelo}: {Length} chars", etapaNome, modelo, resposta.Length);

            await _langfuse.FinalizarTraceAsync(traceContext, instrucoes, resposta, ct);

            return resposta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar agente ({Etapa}) com modelo {Modelo}", etapaNome, modelo);
            throw;
        }
    }

    public static string? ExtrairJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var primeiro = texto.IndexOf('{');
        var ultimo = texto.LastIndexOf('}');

        if (primeiro >= 0 && ultimo > primeiro)
        {
            return texto.Substring(primeiro, ultimo - primeiro + 1);
        }

        return null;
    }
}
