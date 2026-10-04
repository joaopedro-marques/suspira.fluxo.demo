using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.Ferramentas;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class OrquestradorLoopServiceTests
{
    private readonly Mock<ILogger<OrquestradorLoopService>> _loggerMock;
    private readonly Mock<IServicoChat> _openRouterMock;
    private readonly Mock<IAgentesCatalogo> _agenteLoaderMock;
    private readonly Mock<IReferenciasCliente> _referenciaLoaderMock;
    private readonly Mock<IAnalisadorImagem> _analisadorImagemMock;
    private readonly FerramentaRegistry _ferramentaRegistry;
    private readonly LoopOptions _loopOptions;
    private readonly EnriquecedorContextoCliente _enriquecedor;
    private readonly OrquestradorLoopService _loop;

    public OrquestradorLoopServiceTests()
    {
        _loggerMock = new Mock<ILogger<OrquestradorLoopService>>();
        _openRouterMock = new Mock<IServicoChat>();
        _agenteLoaderMock = new Mock<IAgentesCatalogo>();
        _referenciaLoaderMock = new Mock<IReferenciasCliente>();
        _analisadorImagemMock = new Mock<IAnalisadorImagem>();
        _ferramentaRegistry = new FerramentaRegistry();

        _loopOptions = new LoopOptions
        {
            MaxTurnos = 8,
            MaxRefacoesQa = 2,
            MensagemForaContexto = "Fora do contexto",
            MensagemFalha = "Falha no loop"
        };

        _enriquecedor = new EnriquecedorContextoCliente(
            _referenciaLoaderMock.Object,
            _analisadorImagemMock.Object,
            Mock.Of<ILogger<EnriquecedorContextoCliente>>());

        _loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(_loopOptions),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);
    }

    private AgenteDefinicao CriarOrquestrador() => new()
    {
        Nome = "Orquestrador",
        ModeloAlvo = "gemini-flash",
        Persona = "persona",
        Papel = "orquestrador"
    };

    private AgenteDefinicao CriarAgente(string nome, string papel = "producao") => new()
    {
        Nome = nome,
        ModeloAlvo = "claude",
        Persona = $"persona {nome}",
        Papel = papel
    };

    [Fact]
    public async Task ExecutarAsync_ForAcaoForaContexto_ShouldReturnFixedMessage()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(CriarOrquestrador());
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var result = await _loop.ExecutarAsync(123, "qual a capital do Brasil?");

        result.RespostaFinal.Should().Be("Fora do contexto");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoResponderDireto_ShouldReturnResponse()
    {
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(CriarOrquestrador());
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"responder_direto\", \"resposta\": \"Marketing e...\"}");

        var result = await _loop.ExecutarAsync(123, "o que e marketing?");

        result.RespostaFinal.Should().Be("Marketing e...");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoChamarAgente_ShouldCallAgentAndContinueLoop()
    {
        var orquestrador = CriarOrquestrador();
        var redator = CriarAgente("Redator");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => ++orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva um post\"}"
                        : "{\"acao\": \"finalizar\", \"entregavel\": \"Post criado com sucesso\"}",
                    "loop_agente_Redator" => "Post criado com sucesso",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "crie um post");

        result.RespostaFinal.Should().Be("Post criado com sucesso");
        result.EtapasExecutadas.Should().Contain("loop_agente_Redator");
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoChamarFerramenta_ShouldCallToolAndContinueLoop()
    {
        var orquestrador = CriarOrquestrador();
        var ferramentaMock = new Mock<IFerramenta>();
        ferramentaMock.Setup(x => x.Nome).Returns("ferramenta_teste");
        ferramentaMock.Setup(x => x.Descricao).Returns("Ferramenta de teste");
        ferramentaMock
            .Setup(x => x.ExecutarAsync(It.IsAny<LoopContext>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resultado da ferramenta");
        _ferramentaRegistry.Registrar(ferramentaMock.Object);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

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
                    "loop_orquestrador" when callCount == 1 => "{\"acao\": \"chamar_ferramenta\", \"ferramenta\": \"ferramenta_teste\", \"parametros\": {\"key\": \"value\"}}",
                    "loop_orquestrador" when callCount == 2 => "{\"acao\": \"finalizar\", \"entregavel\": \"Resultado da ferramenta\"}",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "use a ferramenta");

        result.RespostaFinal.Should().Be("Resultado da ferramenta");
        ferramentaMock.Verify(x => x.ExecutarAsync(It.IsAny<LoopContext>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_ForAcaoFinalizar_ShouldAutoCallQualidadeAndApprove()
    {
        var orquestrador = CriarOrquestrador();
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

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
                    "loop_orquestrador" => "{\"acao\": \"finalizar\", \"entregavel\": \"Entregavel final\"}",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Entregavel final");
        result.EtapasExecutadas.Should().Contain("loop_qualidade");
    }

    [Fact]
    public async Task ExecutarAsync_QualidadeReprova_ShouldInjectFeedbackAndContinue()
    {
        var orquestrador = CriarOrquestrador();
        var redator = CriarAgente("Redator");
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
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
                    "loop_orquestrador" when qualityCallCount == 0 => "{\"acao\": \"finalizar\", \"entregavel\": \"Entregavel ruim\"}",
                    "loop_qualidade" => ++qualityCallCount == 1
                        ? "{\"aprovado\": false, \"feedback\": \"Melhore o texto\"}"
                        : "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    "loop_orquestrador" when qualityCallCount == 1 => "{\"acao\": \"finalizar\", \"entregavel\": \"Entregavel melhorado\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Entregavel melhorado");
        qualityCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecutarAsync_MaxRefacoesQaExceeded_ShouldReturnFailureMessage()
    {
        var orquestrador = CriarOrquestrador();
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => "{\"acao\": \"finalizar\", \"entregavel\": \"Entregavel\"}",
                    "loop_qualidade" => "{\"aprovado\": false, \"feedback\": \"Ruim\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Falha no loop");
    }

    [Fact]
    public async Task ExecutarAsync_MaxTurnosExceeded_ShouldReturnFailureMessage()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"chamar_agente\", \"agente\": \"Inexistente\", \"briefing\": \"x\"}");

        var options = new LoopOptions
        {
            MaxTurnos = 2,
            MaxRefacoesQa = 2,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        var result = await loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Falha no loop");
    }

    [Fact]
    public async Task ExecutarAsync_DuplicateConsecutiveAction_ShouldInjectWarning()
    {
        var orquestrador = CriarOrquestrador();
        var redator = CriarAgente("Redator");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
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
                    "loop_orquestrador" when callCount == 1 => "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva\"}",
                    "loop_agente_Redator" => "Output",
                    "loop_orquestrador" when callCount == 2 => "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva\"}",
                    "loop_orquestrador" when callCount == 3 => "{\"acao\": \"finalizar\", \"entregavel\": \"Output\"}",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Output");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidOrchestratorJson_ShouldRetryOnceThenFallback()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        var callCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                callCount++;
                return callCount == 1 ? "JSON invalido" : "{\"acao\": \"responder_direto\", \"resposta\": \"Fallback\"}";
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Fallback");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecutarAsync_ClienteIdentificado_ShouldInjectReferences()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());
        _referenciaLoaderMock.Setup(x => x.ListarClientes()).Returns(new List<string> { "acme" }.AsReadOnly());
        _referenciaLoaderMock.Setup(x => x.ObterReferenciasTexto("acme")).Returns("Manual de marca: cor #FF6B35");
        _referenciaLoaderMock.Setup(x => x.ListarImagens("acme")).Returns(new List<string>().AsReadOnly());

        var callCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                callCount++;
                return callCount == 1
                    ? "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Post\", \"cliente\": \"acme\"}"
                    : "{\"acao\": \"responder_direto\", \"resposta\": \"OK\"}";
            });

        var result = await _loop.ExecutarAsync(123, "post para acme");

        result.RespostaFinal.Should().Be("OK");
        _referenciaLoaderMock.Verify(x => x.ObterReferenciasTexto("acme"), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_ClienteNaoRegistrado_ShouldNotInjectReferences()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());
        _referenciaLoaderMock.Setup(x => x.ListarClientes()).Returns(new List<string> { "acme" }.AsReadOnly());
        _referenciaLoaderMock.Setup(x => x.ObterReferenciasTexto("enel")).Returns("deveria nao ser chamado");

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"responder_direto\", \"resposta\": \"OK\", \"cliente\": \"enel\"}");

        var result = await _loop.ExecutarAsync(123, "post para enel");

        result.RespostaFinal.Should().Be("OK");
        _referenciaLoaderMock.Verify(x => x.ObterReferenciasTexto("enel"), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_ShouldCallProgressCallback()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        var progressMessages = new List<string>();
        await _loop.ExecutarAsync(123, "mensagem", async (msg) => progressMessages.Add(msg));

        progressMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecutarAsync_InvalidJsonTwice_ShouldLogWarning()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JSON invalido");

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Falha no loop");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecutarAsync_MaxTurnosExceeded_ShouldLogWarning()
    {
        var orquestrador = CriarOrquestrador();

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"chamar_agente\", \"agente\": \"Inexistente\", \"briefing\": \"x\"}");

        var options = new LoopOptions
        {
            MaxTurnos = 2,
            MaxRefacoesQa = 2,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        var result = await loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Falha no loop");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecutarAsync_MaxRefacoesQaExceeded_ShouldLogWarning()
    {
        var orquestrador = CriarOrquestrador();
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => "{\"acao\": \"finalizar\", \"entregavel\": \"Entregavel\"}",
                    "loop_qualidade" => "{\"aprovado\": false, \"feedback\": \"Ruim\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "mensagem");

        result.RespostaFinal.Should().Be("Falha no loop");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecutarAsync_OrchestratorCall_ShouldPassExplicitMaxTokens()
    {
        var orquestrador = CriarOrquestrador();
        var options = new LoopOptions
        {
            MaxTurnos = 8,
            MaxRefacoesQa = 2,
            MaxTokensOrquestrador = 4000,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "loop_orquestrador", It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"acao\": \"fora_contexto\"}");

        await loop.ExecutarAsync(123, "mensagem");

        _openRouterMock.Verify(
            x => x.ChamarAgenteAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "loop_orquestrador",
                It.IsAny<double>(),
                4000,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_AgentOutputExceedsMaxCharsResultado_ShouldTruncateInTranscript()
    {
        var orquestrador = CriarOrquestrador();
        var redator = CriarAgente("Redator");
        var options = new LoopOptions
        {
            MaxTurnos = 8,
            MaxRefacoesQa = 2,
            MaxCharsResultado = 100,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        var hugeOutput = new string('x', 500);
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
                    "loop_orquestrador" when callCount == 1 => "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva\"}",
                    "loop_agente_Redator" => hugeOutput,
                    "loop_orquestrador" when callCount == 2 => "{\"acao\": \"finalizar\", \"entregavel\": \"OK\"}",
                    "loop_qualidade" => "{\"aprovado\": true}",
                    _ => ""
                };
            });

        await loop.ExecutarAsync(123, "mensagem");

        var secondOrqCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_orquestrador")
            .Skip(1)
            .First();
        var transcriptSent = secondOrqCall.Arguments[3].ToString();
        transcriptSent.Should().NotContain(hugeOutput);
        transcriptSent.Length.Should().BeLessThan(hugeOutput.Length + 500);
    }

    [Fact]
    public async Task ExecutarAsync_TranscriptExceedsMaxCharsContexto_ShouldTruncate()
    {
        var orquestrador = CriarOrquestrador();
        var options = new LoopOptions
        {
            MaxTurnos = 8,
            MaxRefacoesQa = 2,
            MaxCharsContexto = 500,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        var callCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"acao\": \"responder_direto\", \"resposta\": \"OK\"}",
                    _ => "{\"acao\": \"fora_contexto\"}"
                };
            });

        await loop.ExecutarAsync(123, new string('y', 1000));

        var firstOrqCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_orquestrador")
            .First();
        var transcriptSent = firstOrqCall.Arguments[3].ToString();
        transcriptSent.Should().Contain("## Estado do trabalho");
        transcriptSent.Should().Contain("Turno 1/8");
    }

    [Fact]
    public async Task ExecutarAsync_FinalizarSemEntregavel_ShouldUsarOutputDoAgente()
    {
        var orquestrador = CriarOrquestrador();
        var dev = CriarAgente("Dev");
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Dev")).Returns(dev);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { dev }.AsReadOnly());

        var codigoHtml = "<html><body><h1>Email completo</h1></body></html>";
        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => ++orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"Crie um email\"}"
                        : "{\"acao\": \"finalizar\", \"entregavel\": \"\"}",
                    "loop_agente_Dev" => codigoHtml,
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "crie um email");

        result.RespostaFinal.Should().Be(codigoHtml);

        var qaCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_qualidade")
            .First();
        var qaInput = qaCall.Arguments[3].ToString();
        qaInput.Should().Contain(codigoHtml);
    }

    [Fact]
    public async Task ExecutarAsync_FinalizarComEntregavel_ShouldUsarEntregavel()
    {
        var orquestrador = CriarOrquestrador();
        var dev = CriarAgente("Dev");
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Dev")).Returns(dev);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { dev }.AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => ++orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"Crie\"}"
                        : "{\"acao\": \"finalizar\", \"entregavel\": \"entregavel formatado\"}",
                    "loop_agente_Dev" => "codigo do dev",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "crie algo");

        result.RespostaFinal.Should().Be("entregavel formatado");
    }

    [Fact]
    public async Task ExecutarAsync_FerramentaRepetidaComParametrosDiferentes_ShouldNaoBloquear()
    {
        var orquestrador = CriarOrquestrador();
        var ferramentaMock = new Mock<IFerramenta>();
        ferramentaMock.Setup(x => x.Nome).Returns("gerar_imagem");
        ferramentaMock.Setup(x => x.Descricao).Returns("Gera imagem");
        var callCount = 0;
        ferramentaMock
            .Setup(x => x.ExecutarAsync(It.IsAny<LoopContext>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LoopContext ctx, System.Text.Json.JsonElement p, CancellationToken ct) =>
            {
                callCount++;
                return $"Imagem {callCount} gerada";
            });
        _ferramentaRegistry.Registrar(ferramentaMock.Object);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                orqCallCount++;
                return orqCallCount switch
                {
                    1 => "{\"acao\": \"chamar_ferramenta\", \"ferramenta\": \"gerar_imagem\", \"parametros\": {\"prompt\": \"gato\"}}",
                    2 => "{\"acao\": \"chamar_ferramenta\", \"ferramenta\": \"gerar_imagem\", \"parametros\": {\"prompt\": \"cachorro\"}}",
                    3 => "{\"acao\": \"finalizar\", \"entregavel\": \"Duas imagens geradas\"}",
                    _ => "{\"aprovado\": true, \"feedback\": \"OK\"}"
                };
            });

        var result = await _loop.ExecutarAsync(123, "gere duas imagens");

        callCount.Should().Be(2);
        result.RespostaFinal.Should().Be("Duas imagens geradas");
    }

    [Fact]
    public async Task ExecutarAsync_AgentCalledAfterPreviousAgent_ShouldIncludePreviousArtifactsInBriefing()
    {
        var orquestrador = CriarOrquestrador();
        var estrategista = CriarAgente("Estrategista");
        var redator = CriarAgente("Redator");
        var qualidade = CriarAgente("Qualidade", "qualidade");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { estrategista, redator }.AsReadOnly());

        var outputEstrategista = "Plano estrategico: focar na etapa de vendas com funil simplificado";
        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => ++orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Estrategista\", \"briefing\": \"Planeje a campanha\"}"
                        : orqCallCount == 2
                            ? "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva a copy\"}"
                            : "{\"acao\": \"finalizar\", \"entregavel\": \"\"}",
                    "loop_agente_Estrategista" => outputEstrategista,
                    "loop_agente_Redator" => "Copy baseada no plano",
                    "loop_qualidade" => "{\"aprovado\": true, \"feedback\": \"OK\"}",
                    _ => ""
                };
            });

        var result = await _loop.ExecutarAsync(123, "crie campanha para Acme Corp etapa de vendas");

        var redatorCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_agente_Redator")
            .First();
        var briefingSent = redatorCall.Arguments[3].ToString();
        briefingSent.Should().Contain("Trabalho previo de outros agentes");
        briefingSent.Should().Contain(outputEstrategista);
        briefingSent.Should().Contain("Estrategista (artefato art_");
    }

    [Fact]
    public async Task ExecutarAsync_TranscriptTruncamento_ShouldManterTurnosRecentes()
    {
        var orquestrador = CriarOrquestrador();
        var dev = CriarAgente("Dev");
        var options = new LoopOptions
        {
            MaxTurnos = 8,
            MaxRefacoesQa = 2,
            MaxCharsContexto = 2000,
            MaxCharsResultado = 5000,
            MensagemFalha = "Falha no loop"
        };

        var loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(options),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Dev")).Returns(dev);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { dev }.AsReadOnly());

        var outputDev = new string('X', 600);
        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                return etapa switch
                {
                    "loop_orquestrador" => ++orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"Crie\"}"
                        : "{\"acao\": \"finalizar\", \"entregavel\": \"OK\"}",
                    "loop_agente_Dev" => outputDev,
                    "loop_qualidade" => "{\"aprovado\": true}",
                    _ => ""
                };
            });

        await loop.ExecutarAsync(123, "mensagem");

        var secondOrqCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_orquestrador")
            .Skip(1)
            .First();
        var transcriptSent = secondOrqCall.Arguments[3].ToString();
        transcriptSent.Should().Contain("artefato art_");
        transcriptSent.Should().Contain("600 chars");
        transcriptSent.Should().Contain("## Estado do trabalho");
    }

    [Fact]
    public async Task ExecutarAsync_ChamarAgente_WhenAgentReturnsJson_TranscriptShouldContainOnlyResumoAndNotas()
    {
        var orquestrador = CriarOrquestrador();
        var redator = CriarAgente("Redator");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { redator }.AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                if (etapa == "loop_orquestrador")
                {
                    orqCallCount++;
                    return orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"Escreva um post\"}"
                        : "{\"acao\": \"finalizar\"}";
                }
                if (etapa == "loop_agente_Redator")
                    return "{\"entregavel\": \"Post completo do redator\", \"notas\": \"Contexto interno do agente\", \"resumo\": \"Post de 1 paragrafo\"}";
                return "{\"aprovado\": true}";
            });

        var result = await _loop.ExecutarAsync(123, "crie um post");

        (result.RespostaFinal == "Post completo do redator").Should().BeTrue(
            "o entregavel deve ser o campo 'entregavel' do JSON, nao o JSON cru");

        var secondOrqCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_orquestrador")
            .Skip(1)
            .First();
        var transcriptSent = secondOrqCall.Arguments[3].ToString();
        transcriptSent.Should().Contain("Post de 1 paragrafo");
        transcriptSent.Should().Contain("Contexto interno do agente");
        transcriptSent.Should().NotContain("Post completo do redator");
    }

    [Fact]
    public async Task ExecutarAsync_ChamarAgente_WithArtefatosIds_ShouldIncludeOnlySelectedEntregaveisInBriefing()
    {
        var orquestrador = CriarOrquestrador();
        var estrategista = CriarAgente("Estrategista");
        var redator = CriarAgente("Redator");
        var dev = CriarAgente("Dev");

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Estrategista")).Returns(estrategista);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Redator")).Returns(redator);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Dev")).Returns(dev);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { estrategista, redator, dev }.AsReadOnly());

        var orqCallCount = 0;
        string? estrategistaArtId = null;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                if (etapa == "loop_orquestrador")
                {
                    orqCallCount++;
                    if (orqCallCount == 1) return "{\"acao\": \"chamar_agente\", \"agente\": \"Estrategista\", \"briefing\": \"plano\"}";
                    if (orqCallCount == 2)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(instrucoes, @"Agente Estrategista produziu artefato (art_\d+)");
                        estrategistaArtId = match.Success ? match.Groups[1].Value : null;
                        return "{\"acao\": \"chamar_agente\", \"agente\": \"Redator\", \"briefing\": \"copy\"}";
                    }
                    if (orqCallCount == 3)
                        return $"{{\"acao\": \"chamar_agente\", \"agente\": \"Dev\", \"briefing\": \"html\", \"artefatos\": [\"{estrategistaArtId}\"]}}";
                    return "{\"acao\": \"finalizar\"}";
                }
                if (etapa == "loop_agente_Estrategista")
                    return "{\"entregavel\": \"ENTREGAVEL_ESTRATEGISTA\", \"notas\": \"NOTA_ESTRATEGISTA\", \"resumo\": \"plano resumo\"}";
                if (etapa == "loop_agente_Redator")
                    return "{\"entregavel\": \"ENTREGAVEL_REDATOR\", \"notas\": \"NOTA_REDATOR\", \"resumo\": \"copy resumo\"}";
                if (etapa == "loop_agente_Dev")
                    return "DEV_OUTPUT";
                return "{\"aprovado\": true}";
            });

        await _loop.ExecutarAsync(123, "email");

        var devCall = _openRouterMock.Invocations
            .Where(i => i.Arguments[4].ToString() == "loop_agente_Dev")
            .First();
        var briefingDev = devCall.Arguments[3].ToString();

        estrategistaArtId.Should().NotBeNull();
        briefingDev.Should().Contain("ENTREGAVEL_ESTRATEGISTA");
        briefingDev.Should().Contain("NOTA_ESTRATEGISTA");
        briefingDev.Should().NotContain("ENTREGAVEL_REDATOR");
        briefingDev.Should().Contain("NOTA_REDATOR");
    }

    [Fact]
    public async Task ExecutarAsync_Finalizar_WhenLastArtifactIsPrompt_ShouldNotUsePromptAsEntregavel()
    {
        var orquestrador = CriarOrquestrador();
        var promptAgent = new AgenteDefinicao
        {
            Nome = "Prompt para Imagens",
            ModeloAlvo = "claude",
            Persona = "persona",
            Papel = "producao",
            Interno = true
        };

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ObterPorNome("Prompt para Imagens")).Returns(promptAgent);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao> { promptAgent }.AsReadOnly());

        var promptText = "A professional marketing photograph of a modern workspace";
        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                if (etapa == "loop_orquestrador")
                {
                    orqCallCount++;
                    return orqCallCount == 1
                        ? "{\"acao\": \"chamar_agente\", \"agente\": \"Prompt para Imagens\", \"briefing\": \"gere um prompt\"}"
                        : "{\"acao\": \"finalizar\"}";
                }
                if (etapa == "loop_agente_Prompt para Imagens")
                    return promptText;
                return "{\"aprovado\": true}";
            });

        var result = await _loop.ExecutarAsync(123, "gere uma imagem");

        (result.RespostaFinal != promptText).Should().BeTrue(
            "o prompt de imagem nao deve ser o entregavel final");
    }

    [Fact]
    public async Task ExecutarAsync_InvalidJsonRetry_ShouldNotConsumeTurn()
    {
        var orquestrador = CriarOrquestrador();
        var options = new LoopOptions
        {
            MaxTurnos = 1,
            MaxRefacoesQa = 2,
            MaxRetriesGratis = 4,
            MensagemFalha = "Falha"
        };
        var loop = new OrquestradorLoopService(
            _loggerMock.Object, TestOptions.Create(options),
            _openRouterMock.Object, _agenteLoaderMock.Object, _referenciaLoaderMock.Object,
            _ferramentaRegistry, _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                if (etapa == "loop_orquestrador")
                {
                    orqCallCount++;
                    if (orqCallCount == 1) return "not json";
                    return "{\"acao\": \"responder_direto\", \"resposta\": \"OK\"}";
                }
                return "";
            });

        var result = await loop.ExecutarAsync(123, "oi");

        result.RespostaFinal.Should().Be("OK",
            "com MaxRetriesGratis=4, o retry nao consome turno; mesmo com MaxTurnos=1, deve haver retry + sucesso");
        orqCallCount.Should().Be(2, "deve haver 2 chamadas ao orquestrador: 1 invalida + 1 valida");
    }

    [Fact]
    public async Task ExecutarAsync_RetryAfterGratisExhausted_ShouldConsumeTurnAndFail()
    {
        var orquestrador = CriarOrquestrador();
        var options = new LoopOptions
        {
            MaxTurnos = 2,
            MaxRefacoesQa = 2,
            MaxRetriesGratis = 0,
            MensagemFalha = "Falha"
        };
        var loop = new OrquestradorLoopService(
            _loggerMock.Object, TestOptions.Create(options),
            _openRouterMock.Object, _agenteLoaderMock.Object, _referenciaLoaderMock.Object,
            _ferramentaRegistry, _enriquecedor);

        _agenteLoaderMock.Setup(x => x.ObterPorPapel("orquestrador")).Returns(orquestrador);
        _agenteLoaderMock.Setup(x => x.ListarAgentesProducao()).Returns(new List<AgenteDefinicao>().AsReadOnly());

        var orqCallCount = 0;
        _openRouterMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chatId, string persona, string modelo, string instrucoes, string etapa, double temp, int tokens, CancellationToken ct) =>
            {
                orqCallCount++;
                return "not json";
            });

        var result = await loop.ExecutarAsync(123, "oi");

        result.RespostaFinal.Should().Be("Falha",
            "com MaxRetriesGratis=0, todos retries consomem turno; apos 2 turnos, falha");
        orqCallCount.Should().Be(2, "com MaxTurnos=2, deve haver 2 chamadas (ambas invalidas)");
    }
}
