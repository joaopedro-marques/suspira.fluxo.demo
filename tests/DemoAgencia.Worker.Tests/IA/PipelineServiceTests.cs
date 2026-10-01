using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Observabilidade;
using DemoAgencia.Worker.Referencias;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA;

public class PipelineServiceTests
{
    private readonly Mock<ILogger<PipelineService>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<OpenRouterService> _openRouterMock;
    private readonly Mock<AgenteLoader> _agenteLoaderMock;
    private readonly Mock<ReferenciaClienteLoader> _referenciaLoaderMock;
    private readonly Mock<HistoricoChat> _historicoMock;
    private readonly IConfiguration _configuration;
    private readonly PipelineService _pipeline;

    public PipelineServiceTests()
    {
        _loggerMock = new Mock<ILogger<PipelineService>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _historicoMock = new Mock<HistoricoChat>();

        var langfuseClientMock = new Mock<LangfuseClient>(
            Mock.Of<ILogger<LangfuseClient>>(),
            Mock.Of<IConfiguration>());

        var anonimizadorMock = new Mock<AnonimizadorService>(Mock.Of<IConfiguration>());
        anonimizadorMock.Setup(x => x.Anonimizar(It.IsAny<string>())).Returns<string>(s => s);

        var langfuseInterceptorMock = new Mock<LangfuseInterceptor>(
            Mock.Of<ILogger<LangfuseInterceptor>>(),
            langfuseClientMock.Object,
            anonimizadorMock.Object);

        _openRouterMock = new Mock<OpenRouterService>(
            Mock.Of<ILogger<OpenRouterService>>(),
            Mock.Of<IConfiguration>(),
            langfuseInterceptorMock.Object,
            Mock.Of<IHttpClientFactory>());

        _agenteLoaderMock = new Mock<AgenteLoader>(
            Mock.Of<ILogger<AgenteLoader>>(),
            Mock.Of<IConfiguration>(),
            (string?)null);

        _referenciaLoaderMock = new Mock<ReferenciaClienteLoader>(
            Mock.Of<ILogger<ReferenciaClienteLoader>>(),
            Mock.Of<IConfiguration>(),
            (string?)null);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pipeline:MaxRefacoes"] = "2",
                ["Pipeline:MensagemForaContexto"] = "Fora do contexto",
                ["Pipeline:MensagemFalhaPipeline"] = "Falha no pipeline"
            })
            .Build();

        _pipeline = new PipelineService(
            _loggerMock.Object,
            _loggerFactoryMock.Object,
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _historicoMock.Object,
            _configuration);
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoForaContexto_ShouldReturnFixedMessage()
    {
        var orquestrador = new AgenteDefinicao
        {
            Nome = "Orquestrador",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "orquestrador"
        };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var result = await _pipeline.ExecutarAsync(123, "qual a capital do Brasil?");

        result.Rota.Should().Be("fora_contexto");
        result.RespostaFinal.Should().Be("Fora do contexto");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoDireta_ShouldReturnOrchestratorResponse()
    {
        var orquestrador = new AgenteDefinicao
        {
            Nome = "Orquestrador",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "orquestrador"
        };
        var formatador = new AgenteDefinicao
        {
            Nome = "Formatador",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "formatacao"
        };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"direta\", \"resposta\": \"Resposta direta\"}");

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "formatador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta formatada");

        var result = await _pipeline.ExecutarAsync(123, "o que e marketing?");

        result.Rota.Should().Be("direta");
        result.RespostaFinal.Should().Be("Resposta formatada");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoPipeline_ShouldRunFullFlow()
    {
        var orquestrador = new AgenteDefinicao
        {
            Nome = "Orquestrador",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "orquestrador"
        };
        var estrategista = new AgenteDefinicao
        {
            Nome = "Estrategista",
            ModeloAlvo = "llama",
            Persona = "persona",
            Papel = "estrategista"
        };
        var redator = new AgenteDefinicao
        {
            Nome = "Redator",
            ModeloAlvo = "claude",
            Persona = "persona",
            Papel = "producao"
        };
        var qualidade = new AgenteDefinicao
        {
            Nome = "Qualidade",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "qualidade"
        };
        var formatador = new AgenteDefinicao
        {
            Nome = "Formatador",
            ModeloAlvo = "gemini-flash",
            Persona = "persona",
            Papel = "formatacao"
        };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        var callCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                callCount++;
                return etapa switch
                {
                    "orquestrador" => "{\"acao\": \"pipeline\", \"briefing\": \"Criar post\"}",
                    "estrategista_planejador" => "{\"agente\": \"Redator\", \"instrucoes\": \"Escreva um post\"}",
                    "producao_Redator" => "Post criado com sucesso",
                    "qualidade" => "{\"veredito\": \"aprovado\", \"feedback\": \"OK\"}",
                    "estrategista_aprovador" => "{\"aprovado\": true, \"observacoes\": \"Aprovado\"}",
                    "formatador" => "Post formatado para Telegram",
                    _ => ""
                };
            });

        var result = await _pipeline.ExecutarAsync(123, "crie um post para Instagram");

        result.Rota.Should().Be("pipeline");
        result.RespostaFinal.Should().Be("Post formatado para Telegram");
        result.EtapasExecutadas.Should().Contain("orquestrador");
        result.EtapasExecutadas.Should().Contain("estrategista_planejador");
        result.EtapasExecutadas.Should().Contain("producao_Redator");
        result.EtapasExecutadas.Should().Contain("qualidade");
        result.EtapasExecutadas.Should().Contain("estrategista_aprovador");
        result.EtapasExecutadas.Should().Contain("formatador");
    }

    [Fact]
    public async Task ExecutarAsync_QualityRejects_ShouldRework()
    {
        var orquestrador = new AgenteDefinicao { Nome = "Orquestrador", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "orquestrador" };
        var estrategista = new AgenteDefinicao { Nome = "Estrategista", ModeloAlvo = "llama", Persona = "p", Papel = "estrategista" };
        var redator = new AgenteDefinicao { Nome = "Redator", ModeloAlvo = "claude", Persona = "p", Papel = "producao" };
        var qualidade = new AgenteDefinicao { Nome = "Qualidade", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "qualidade" };
        var formatador = new AgenteDefinicao { Nome = "Formatador", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "formatacao" };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("formatacao")).Returns(formatador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        var qualityCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "orquestrador" => "{\"acao\": \"pipeline\", \"briefing\": \"Criar post\"}",
                    "estrategista_planejador" => "{\"agente\": \"Redator\", \"instrucoes\": \"Escreva um post\"}",
                    "producao_Redator" => "Post criado",
                    "qualidade" => ++qualityCallCount == 1
                        ? "{\"veredito\": \"reprovado\", \"feedback\": \"Melhore o texto\"}"
                        : "{\"veredito\": \"aprovado\", \"feedback\": \"OK\"}",
                    "estrategista_aprovador" => "{\"aprovado\": true, \"observacoes\": \"Aprovado\"}",
                    "formatador" => "Post final",
                    _ => ""
                };
            });

        var result = await _pipeline.ExecutarAsync(123, "crie um post");

        result.RespostaFinal.Should().Be("Post final");
        qualityCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecutarAsync_MaxRetriesExceeded_ShouldReturnFailureMessage()
    {
        var orquestrador = new AgenteDefinicao { Nome = "Orquestrador", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "orquestrador" };
        var estrategista = new AgenteDefinicao { Nome = "Estrategista", ModeloAlvo = "llama", Persona = "p", Papel = "estrategista" };
        var redator = new AgenteDefinicao { Nome = "Redator", ModeloAlvo = "claude", Persona = "p", Papel = "producao" };
        var qualidade = new AgenteDefinicao { Nome = "Qualidade", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "qualidade" };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "orquestrador" => "{\"acao\": \"pipeline\", \"briefing\": \"Criar post\"}",
                    "estrategista_planejador" => "{\"agente\": \"Redator\", \"instrucoes\": \"Escreva\"}",
                    "producao_Redator" => "Post",
                    "qualidade" => "{\"veredito\": \"reprovado\", \"feedback\": \"Ruim\"}",
                    "estrategista_aprovador" => "{\"aprovado\": false, \"observacoes\": \"Reprovar\"}",
                    _ => ""
                };
            });

        var result = await _pipeline.ExecutarAsync(123, "crie um post");

        result.RespostaFinal.Should().Be("Falha no pipeline");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidOrchestratorJson_ShouldFallbackToDireta()
    {
        var orquestrador = new AgenteDefinicao { Nome = "Orquestrador", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "orquestrador" };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta invalida sem JSON");

        var result = await _pipeline.ExecutarAsync(123, "qualquer mensagem");

        result.Rota.Should().Be("direta");
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var orquestrador = new AgenteDefinicao { Nome = "Orquestrador", ModeloAlvo = "gemini-flash", Persona = "p", Papel = "orquestrador" };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var progressMessages = new List<string>();
        await _pipeline.ExecutarAsync(123, "mensagem", async (msg) => progressMessages.Add(msg));

        progressMessages.Should().Contain("🧠 Analisando seu pedido...");
    }
}

