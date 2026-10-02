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

        _loop = new OrquestradorLoopService(
            _loggerMock.Object,
            TestOptions.Create(_loopOptions),
            _openRouterMock.Object,
            _agenteLoaderMock.Object,
            _referenciaLoaderMock.Object,
            _ferramentaRegistry,
            _analisadorImagemMock.Object);
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
            _analisadorImagemMock.Object);

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
}
