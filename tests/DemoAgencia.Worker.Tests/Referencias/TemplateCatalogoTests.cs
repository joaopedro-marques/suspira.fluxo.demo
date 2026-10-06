using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DemoAgencia.Worker.Tests.Referencias;

public class TemplateCatalogoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<TemplateCatalogo>> _loggerMock;

    public TemplateCatalogoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<ILogger<TemplateCatalogo>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StartAsync_WithHtmlAndSidecar_LoadsTemplate()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_exemplo.html"), "<html>ACME</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_exemplo.json"), """{"cliente":"acme","fase":"pre-chaves","padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.Obter("ACME_exemplo").Should().Be("<html>ACME</html>");
    }

    [Fact]
    public async Task Selecionar_ByFaseMatch_ReturnsCorrectTemplate()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","padrao":false}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_vistoria.html"), "<html>vistoria</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_vistoria.json"), """{"cliente":"acme","fase":"pre-chaves","padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("acme", "pos-chaves", null, null);

        id.Should().Be("ACME_visita");
        catalogo.Obter(id).Should().Be("<html>visita</html>");
    }

    [Fact]
    public async Task Selecionar_BySubJornadaMatch_ReturnsCorrectTemplate()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","sub_jornadas":["assistencia","visita-tecnica"],"padrao":false}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_outro.html"), "<html>outro</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_outro.json"), """{"cliente":"acme","fase":"pos-chaves","sub_jornadas":["outro"],"padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("acme", "pos-chaves", "assistencia", null);

        id.Should().Be("ACME_visita");
    }

    [Fact]
    public async Task Selecionar_ByPalavrasChaveMatch_ReturnsCorrectTemplate()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","palavras_chave":["visita tecnica","reparo"],"padrao":false}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_outro.html"), "<html>outro</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_outro.json"), """{"cliente":"acme","fase":"pos-chaves","palavras_chave":["outro"],"padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("acme", null, null, "preciso agendar visita tecnica para reparo");

        id.Should().Be("ACME_visita");
    }

    [Fact]
    public async Task Selecionar_NoMatch_ReturnsDefault()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","padrao":false}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_default.html"), "<html>default</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_default.json"), """{"cliente":"acme","fase":"pre-chaves","padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("acme", "jornada-inexistente", null, null);

        id.Should().Be("ACME_default");
    }

    [Fact]
    public async Task Selecionar_WrongClient_ReturnsDefault()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","padrao":false}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_default.html"), "<html>default</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_default.json"), """{"cliente":"acme","padrao":true}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("beta", "pos-chaves", null, null);

        id.Should().Be("ACME_default");
    }

    [Fact]
    public async Task Selecionar_NoPadrao_ReturnsFirstAlphabetically()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_b.html"), "<html>b</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_b.json"), """{"cliente":"acme"}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_a.html"), "<html>a</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_a.json"), """{"cliente":"acme"}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var id = catalogo.Selecionar("beta", null, null, null);

        id.Should().Be("ACME_a");
    }

    [Fact]
    public async Task Obter_InvalidId_ReturnsNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme"}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.Obter("nao-existe").Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WithoutSidecar_LoadsHtmlWithNullMetadata()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_sem_json.html"), "<html>sem json</html>");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.Obter("ACME_sem_json").Should().Be("<html>sem json</html>");
    }

    [Fact]
    public async Task StartAsync_IgnoresJsonWithoutMatchingHtml()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_orfao.json"), """{"cliente":"acme"}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.Obter("ACME_orfao").Should().BeNull();
    }

    [Fact]
    public async Task ObterSubJornadas_WithValidId_ReturnsSubJornadas()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves","sub_jornadas":["assistencia","visita-tecnica"]}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        var subs = catalogo.ObterSubJornadas("ACME_visita");

        subs.Should().BeEquivalentTo(new[] { "assistencia", "visita-tecnica" });
    }

    [Fact]
    public async Task ObterSubJornadas_WithInvalidId_ReturnsEmpty()
    {
        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.ObterSubJornadas("nao-existe").Should().BeEmpty();
    }

    [Fact]
    public async Task ObterSubJornadas_WithoutSubJornadasInSidecar_ReturnsEmpty()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.html"), "<html>visita</html>");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ACME_visita.json"), """{"cliente":"acme","fase":"pos-chaves"}""");

        var catalogo = new TemplateCatalogo(_loggerMock.Object, _tempDir);
        await catalogo.StartAsync(CancellationToken.None);

        catalogo.ObterSubJornadas("ACME_visita").Should().BeEmpty();
    }
}
