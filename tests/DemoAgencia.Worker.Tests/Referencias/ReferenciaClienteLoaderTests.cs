using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Referencias;

public class ReferenciaClienteLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<ReferenciaClienteLoader>> _loggerMock;
    private readonly IConfiguration _configuration;

    public ReferenciaClienteLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<ILogger<ReferenciaClienteLoader>>();
        _configuration = new ConfigurationBuilder().Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task StartAsync_WithValidFiles_ShouldGroupByClient()
    {
        var acmeMarca = """{"cores": ["#FF0000", "#00FF00"], "tom": "moderno"}""";
        var acmeExemplo = "<html><body>Exemplo</body></html>";
        var betaMarca = """{"cores": ["#0000FF"], "tom": "formal"}""";

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), acmeMarca);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_exemplo.html"), acmeExemplo);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_beta_marca.json"), betaMarca);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("acme").Should().NotBeEmpty();
        loader.ObterReferenciasTexto("beta").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ObterReferenciasTexto_ShouldReturnFormattedBlock()
    {
        var marcaContent = """{"cores": ["#FF0000"], "tom": "moderno"}""";
        var exemploContent = "<html><body>Exemplo</body></html>";

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), marcaContent);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_exemplo.html"), exemploContent);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var referencias = loader.ObterReferenciasTexto("acme");

        referencias.Should().Contain("marca.json");
        referencias.Should().Contain(marcaContent);
        referencias.Should().Contain("exemplo.html");
        referencias.Should().Contain(exemploContent);
    }

    [Fact]
    public async Task ObterReferenciasTexto_ShouldTruncateAtConfiguredLimit()
    {
        var longContent = new string('A', 5000);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), longContent);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pipeline:Referencias:MaxCharsPorArquivo"] = "100"
            })
            .Build();

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, config, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var referencias = loader.ObterReferenciasTexto("acme");

        referencias.Should().Contain("[truncado");
        referencias.Length.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task ListarImagens_ShouldReturnImagePaths()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "CLIENTE_acme_logo.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "CLIENTE_acme_banner.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var imagens = loader.ListarImagens("acme");

        imagens.Should().HaveCount(2);
        imagens.Should().Contain(i => i.EndsWith("logo.png"));
        imagens.Should().Contain(i => i.EndsWith("banner.jpg"));
    }

    [Fact]
    public async Task StartAsync_ShouldIgnoreFilesWithoutPrefix()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "random_file.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "another.md"), "content");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("acme").Should().NotBeEmpty();
    }

    [Fact]
    public async Task StartAsync_WithEmptyDirectory_ShouldNotThrow()
    {
        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_WithMissingDirectory_ShouldNotThrow()
    {
        var missingDir = Path.Combine(_tempDir, "nonexistent");
        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, missingDir);

        var act = () => loader.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ObterReferenciasTexto_WithUnknownClient_ShouldReturnEmpty()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var referencias = loader.ObterReferenciasTexto("unknown");

        referencias.Should().BeEmpty();
    }

    [Fact]
    public async Task ListarImagens_WithUnknownClient_ShouldReturnEmpty()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var imagens = loader.ListarImagens("unknown");

        imagens.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldNormalizeClientName()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_Acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CLIENTE_BETA_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("acme").Should().NotBeEmpty();
        loader.ObterReferenciasTexto("beta").Should().NotBeEmpty();
    }
}
