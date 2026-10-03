using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.IA.OrquestradorLoop;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class EnriquecedorContextoClienteTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IReferenciasCliente> _referenciasMock;
    private readonly Mock<IAnalisadorImagem> _analisadorMock;
    private readonly Mock<ILogger<EnriquecedorContextoCliente>> _loggerMock;

    public EnriquecedorContextoClienteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _referenciasMock = new Mock<IReferenciasCliente>();
        _analisadorMock = new Mock<IAnalisadorImagem>();
        _loggerMock = new Mock<ILogger<EnriquecedorContextoCliente>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ObterContextoAsync_WithTextRefs_ShouldIncludeText()
    {
        _referenciasMock.Setup(x => x.ObterReferenciasTexto("acme")).Returns("Manual de marca: cor #FF0000");
        _referenciasMock.Setup(x => x.ListarImagens("acme")).Returns(new List<string>().AsReadOnly());

        var enriquecedor = new EnriquecedorContextoCliente(_referenciasMock.Object, _analisadorMock.Object, _loggerMock.Object);
        var contexto = await enriquecedor.ObterContextoAsync("acme");

        contexto.Should().Contain("Manual de marca");
    }

    [Fact]
    public async Task ObterContextoAsync_WithImageRefs_ShouldIncludeDescriptions()
    {
        var imagePath = Path.Combine(_tempDir, "logo.png");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        _referenciasMock.Setup(x => x.ObterReferenciasTexto("acme")).Returns("");
        _referenciasMock.Setup(x => x.ListarImagens("acme")).Returns(new List<string> { imagePath }.AsReadOnly());
        _analisadorMock.Setup(x => x.DescreverImagemAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Logo circular com fundo azul");

        var enriquecedor = new EnriquecedorContextoCliente(_referenciasMock.Object, _analisadorMock.Object, _loggerMock.Object);
        var contexto = await enriquecedor.ObterContextoAsync("acme");

        contexto.Should().Contain("Logo circular");
        contexto.Should().Contain("logo.png");
    }

    [Fact]
    public async Task ObterContextoAsync_WithBothRefs_ShouldIncludeBoth()
    {
        var imagePath = Path.Combine(_tempDir, "banner.jpg");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0xFF, 0xD8, 0xFF });

        _referenciasMock.Setup(x => x.ObterReferenciasTexto("acme")).Returns("Cores: vermelho");
        _referenciasMock.Setup(x => x.ListarImagens("acme")).Returns(new List<string> { imagePath }.AsReadOnly());
        _analisadorMock.Setup(x => x.DescreverImagemAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Banner com texto promocional");

        var enriquecedor = new EnriquecedorContextoCliente(_referenciasMock.Object, _analisadorMock.Object, _loggerMock.Object);
        var contexto = await enriquecedor.ObterContextoAsync("acme");

        contexto.Should().Contain("Cores: vermelho");
        contexto.Should().Contain("Banner com texto");
    }

    [Fact]
    public async Task ObterContextoAsync_WithNoRefs_ShouldReturnEmpty()
    {
        _referenciasMock.Setup(x => x.ObterReferenciasTexto("unknown")).Returns("");
        _referenciasMock.Setup(x => x.ListarImagens("unknown")).Returns(new List<string>().AsReadOnly());

        var enriquecedor = new EnriquecedorContextoCliente(_referenciasMock.Object, _analisadorMock.Object, _loggerMock.Object);
        var contexto = await enriquecedor.ObterContextoAsync("unknown");

        contexto.Should().BeEmpty();
    }
}
