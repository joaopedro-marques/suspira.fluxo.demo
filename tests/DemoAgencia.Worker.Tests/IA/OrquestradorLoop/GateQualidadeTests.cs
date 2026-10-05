using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class GateQualidadeTests
{
    [Fact]
    public async Task AvaliarAsync_WithInfoDeck_ShouldIncludeInQaPrompt()
    {
        var chatMock = new Mock<IServicoChat>();
        var agentesMock = new Mock<IAgentesCatalogo>();
        var qualidade = new AgenteDefinicao
        {
            Nome = "Qualidade", ModeloAlvo = "claude", Persona = "persona", Papel = "qualidade", Temperatura = 0.3
        };
        agentesMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        chatMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true, \"feedback\": \"OK\"}");

        var gate = new GateQualidade(chatMock.Object, agentesMock.Object);
        var infoDeck = "Plano: capa, slide_1\nGeradas: [img_1] capa, [img_2] slide_1";

        await gate.AvaliarAsync(123, "briefing", "entregavel", infoDeck);

        chatMock.Verify(x => x.ChamarAgenteAsync(
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<string>(p => p.Contains(infoDeck) && p.Contains("Inventario de imagens")),
            It.IsAny<string>(),
            It.IsAny<double>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AvaliarAsync_WithoutInfoDeck_ShouldNotIncludeInQaPrompt()
    {
        var chatMock = new Mock<IServicoChat>();
        var agentesMock = new Mock<IAgentesCatalogo>();
        var qualidade = new AgenteDefinicao
        {
            Nome = "Qualidade", ModeloAlvo = "claude", Persona = "persona", Papel = "qualidade", Temperatura = 0.3
        };
        agentesMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        chatMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true, \"feedback\": \"OK\"}");

        var gate = new GateQualidade(chatMock.Object, agentesMock.Object);

        await gate.AvaliarAsync(123, "briefing", "entregavel");

        chatMock.Verify(x => x.ChamarAgenteAsync(
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<string>(p => !p.Contains("Inventario de imagens")),
            It.IsAny<string>(),
            It.IsAny<double>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AvaliarAsync_WhenNoQualidadeAgent_ShouldApprove()
    {
        var chatMock = new Mock<IServicoChat>();
        var agentesMock = new Mock<IAgentesCatalogo>();
        agentesMock.Setup(x => x.ObterPorPapel("qualidade")).Returns((AgenteDefinicao?)null);

        var gate = new GateQualidade(chatMock.Object, agentesMock.Object);

        var result = await gate.AvaliarAsync(123, "briefing", "entregavel", "deck info");

        result.Aprovado.Should().BeTrue();
    }

    [Fact]
    public async Task AvaliarAsync_WithPedidoOriginal_ShouldIncludeInQaPrompt()
    {
        var chatMock = new Mock<IServicoChat>();
        var agentesMock = new Mock<IAgentesCatalogo>();
        var qualidade = new AgenteDefinicao
        {
            Nome = "Qualidade", ModeloAlvo = "claude", Persona = "persona", Papel = "qualidade", Temperatura = 0.3
        };
        agentesMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        chatMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"aprovado\": true, \"feedback\": \"OK\"}");

        var gate = new GateQualidade(chatMock.Object, agentesMock.Object);

        await gate.AvaliarAsync(123, "briefing", "entregavel", pedidoOriginal: "post de Instagram para Acme");

        chatMock.Verify(x => x.ChamarAgenteAsync(
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<string>(p => p.Contains("Pedido original do usuario") && p.Contains("post de Instagram para Acme")),
            It.IsAny<string>(),
            It.IsAny<double>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AvaliarAsync_WhenQaResponseInvalidJson_ShouldReprove()
    {
        var chatMock = new Mock<IServicoChat>();
        var agentesMock = new Mock<IAgentesCatalogo>();
        var qualidade = new AgenteDefinicao
        {
            Nome = "Qualidade", ModeloAlvo = "claude", Persona = "persona", Papel = "qualidade", Temperatura = 0.3
        };
        agentesMock.Setup(x => x.ObterPorPapel("qualidade")).Returns(qualidade);

        chatMock
            .Setup(x => x.ChamarAgenteAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Resposta do QA sem JSON valido");

        var gate = new GateQualidade(chatMock.Object, agentesMock.Object);

        var result = await gate.AvaliarAsync(123, "briefing", "entregavel");

        result.Aprovado.Should().BeFalse();
    }
}
