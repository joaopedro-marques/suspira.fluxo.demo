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

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), acmeMarca);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_exemplo.html"), acmeExemplo);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "beta_marca.json"), betaMarca);

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

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), marcaContent);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_exemplo.html"), exemploContent);

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
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), longContent);

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
    public async Task StartAsync_ShouldIgnoreFilesWithoutUnderscore()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "randomfile.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "another.md"), "content");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("acme").Should().NotBeEmpty();
        loader.ObterReferenciasTexto("randomfile").Should().BeEmpty();
        loader.ListarClientes().Should().NotContain("another");
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
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var referencias = loader.ObterReferenciasTexto("unknown");

        referencias.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldNormalizeClientName()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "BETA_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("acme").Should().NotBeEmpty();
        loader.ObterReferenciasTexto("beta").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListarAssets_WithTypedAssets_ShouldParseTypeFromFilename()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_header_principal.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_footer_padrao.png"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_icon_logo.png"), new byte[] { 3 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("acme");

        assets.Should().HaveCount(3);
        assets.Should().Contain(a => a.Tipo == TipoAsset.Header && a.Nome == "principal");
        assets.Should().Contain(a => a.Tipo == TipoAsset.Footer && a.Nome == "padrao");
        assets.Should().Contain(a => a.Tipo == TipoAsset.Icon && a.Nome == "logo");
    }

    [Fact]
    public async Task ListarAssets_WithLegacyImageName_ShouldInferTypeFromSecondPart()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_logo.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_foto_equipe.jpg"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("acme");

        assets.Should().HaveCount(2);
        assets.Should().Contain(a => a.Tipo == TipoAsset.Logo);
        assets.Should().Contain(a => a.Tipo == TipoAsset.Foto);
    }

    [Fact]
    public async Task ListarAssets_WithUnknownClient_ShouldReturnEmpty()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_header_main.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("unknown");

        assets.Should().BeEmpty();
    }

    [Fact]
    public async Task ListarAssets_ShouldIncludeIdAndCliente()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "acme_header_main.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("acme");

        assets.Should().HaveCount(1);
        assets.First().Id.Should().NotBeEmpty();
        assets.First().Cliente.Should().Be("acme");
    }

    [Fact]
    public async Task ListarClientes_WithMultipleClients_ShouldReturnAll()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "beta_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var clientes = loader.ListarClientes();

        clientes.Should().HaveCount(2);
        clientes.Should().Contain("acme");
        clientes.Should().Contain("beta");
    }

    [Fact]
    public async Task ListarClientes_WithNoFiles_ShouldReturnEmpty()
    {
        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var clientes = loader.ListarClientes();

        clientes.Should().BeEmpty();
    }

    [Fact]
    public async Task ListarClientes_ShouldReturnLowercase()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Acme_marca.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "BETA_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var clientes = loader.ListarClientes();

        clientes.Should().Contain("acme");
        clientes.Should().Contain("beta");
    }

    [Fact]
    public async Task StartAsync_WithMrvFiles_ShouldRegisterAsMrvClient()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "MRV_html_visitatecnica.html"), "<html>visita</html>");
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "MRV_logoMRV.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "MRV_logo_sensia.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var clientes = loader.ListarClientes();
        clientes.Should().Contain("mrv");

        loader.ObterReferenciasTexto("mrv").Should().Contain("visita");
        loader.ListarAssets("mrv").Should().HaveCount(2);
    }

    [Fact]
    public async Task ObterEstrategia_WithEstrategiaSubfolder_ShouldReturnTypedData()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);

        var paleta = """
        {
          "paleta_de_cores": [
            {
              "cor_principal": "Roxo",
              "etapa": "jornada pos-compra",
              "descricao": "Transformacao e sonho",
              "cores_hex_aproximadas": [["#784099"], ["#AB40D9"]]
            },
            {
              "cor_principal": "Rosa",
              "etapa": "jornada pre-chaves",
              "descricao": "Acolhimento e cuidado",
              "cores_hex_aproximadas": [["#F7297D"], ["#FF5AAD"]]
            }
          ]
        }
        """;

        var temas = """
        {
          "titulo": "Mapeamento da Jornada",
          "fases": [
            { "nome": "1. Pos-compras", "temas": ["Boas vindas", "Financeiro"] },
            { "nome": "2. Pre-chaves", "temas": ["Financeiro", "Vistoria"] }
          ]
        }
        """;

        var jornada = """
        {
          "Adquirir": {
            "Jornada-pos-compra": { "1": "Pos Financiamento", "2": "6 meses Pos" }
          },
          "Acompanhar": {
            "Jornada-pre-chaves": { "1": "Vistoria Antecipada", "2": "Entrega das chaves" }
          }
        }
        """;

        var mapa = """
        {
          "titulo": "Experiencia e uma Jornada!",
          "legenda": { "E": "Emocao", "R": "Razao" },
          "jornada": [
            { "etapa": "PESQUISA", "fator_decisao": "ER", "sentimentos": ["EMPOLGACAO"] },
            { "etapa": "POS VENDA", "fator_decisao": "ER", "resultado": ["SATISFACAO", "INSATISFACAO"] }
          ]
        }
        """;

        var satisfacoes = """
        {
          "titulo": "Mapeamento",
          "insatisfacoes": {
            "categorias": [{ "nome": "Entrega", "itens": ["Demora", "Atraso"] }]
          },
          "satisfacoes": {
            "categorias": [{ "nome": "Atendimento", "itens": ["Geral", "Elogio"] }]
          }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paleta);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_temas_jornadas.json"), temas);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_jornada_cliente.json"), jornada);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_mapa_emocional_jornada.json"), mapa);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_satisfacoes_insatisfacoes.json"), satisfacoes);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia.Should().NotBeNull();
        estrategia!.Cliente.Should().Be("mrv");
        estrategia.Fases.Should().ContainKey("pos-compra");
        estrategia.Fases.Should().ContainKey("pre-chaves");
        estrategia.Fases["pos-compra"].CorPrincipal.Should().Be("Roxo");
        estrategia.Fases["pos-compra"].Temas.Should().Contain("Boas vindas");
        estrategia.Fases["pre-chaves"].CorPrincipal.Should().Be("Rosa");
        estrategia.MapaEmocional.Should().HaveCount(2);
        estrategia.Satisfacoes.Should().HaveCount(1);
        estrategia.Insatisfacoes.Should().HaveCount(1);
    }

    [Fact]
    public async Task ObterEstrategia_WithNoEstrategia_ShouldReturnNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("acme");

        estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ObterEstrategia_WithUnknownClient_ShouldReturnNull()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        var paletaValida = """
        { "paleta_de_cores": [
            { "cor_principal": "Roxo", "etapa": "jornada pos-compra", "descricao": "", "cores_hex_aproximadas": [] }
        ] }
        """;
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paletaValida);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("unknown");

        estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ListarClientes_WithStrategyOnlyClient_ShouldIncludeClient()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        var paletaValida = """
        { "paleta_de_cores": [
            { "cor_principal": "Roxo", "etapa": "jornada pos-compra", "descricao": "", "cores_hex_aproximadas": [] }
        ] }
        """;
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paletaValida);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ListarClientes().Should().Contain("mrv");
    }

    [Fact]
    public async Task FaseEstrategia_ShouldMergeSubJornadasFromJornadaCliente()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);

        var jornada = """
        {
          "Adquirir": {
            "Jornada-pos-compra": { "1": "Pos Financiamento" }
          },
          "Acompanhar": {
            "Jornada-pos-compra": { "1": "Visita a obra" },
            "Jornada-pre-chaves": { "1": "Vistoria", "2": "Entrega das chaves" }
          }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_jornada_cliente.json"), jornada);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), """
        { "paleta_de_cores": [
            { "cor_principal": "Roxo", "etapa": "jornada pos-compra", "descricao": "", "cores_hex_aproximadas": [] },
            { "cor_principal": "Rosa", "etapa": "jornada pre-chaves", "descricao": "", "cores_hex_aproximadas": [] }
        ] }
        """);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia.Should().NotBeNull();
        estrategia!.Fases["pos-compra"].SubJornadas.Should().ContainKey("Jornada-pos-compra");
        estrategia.Fases["pos-compra"].SubJornadas["Jornada-pos-compra"].Should().Contain("Pos Financiamento");
        estrategia.Fases["pos-compra"].SubJornadas["Jornada-pos-compra"].Should().Contain("Visita a obra");
        estrategia.Fases["pre-chaves"].SubJornadas.Should().ContainKey("Jornada-pre-chaves");
        estrategia.Fases["pre-chaves"].SubJornadas["Jornada-pre-chaves"].Should().HaveCount(2);
    }

    [Fact]
    public async Task ListarAssets_WithBannerDirectory_ShouldClassifyAsBanner()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_entrega_de_chaves.png"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "MRV_logo.png"), new byte[] { 3 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("mrv");

        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Banner && a.Nome == "agendar_vistoria");
        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Banner && a.Nome == "entrega_de_chaves");
        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Logo);
    }

    [Fact]
    public async Task ListarAssets_WithBannerNameSegment_ShouldClassifyAsBanner()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_banner_financiamento_banco.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("mrv");

        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Banner && a.Nome == "financiamento_banco");
    }

    [Fact]
    public async Task SelecionarBanner_WithTemplateAffinity_ShouldMatchStrongest()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        var jornada = """
        { "Acompanhar": { "Jornada-pre-chaves": { "1": "Vistoria Antecipada", "2": "Entrega das chaves" } } }
        """;
        var paleta = """
        { "paleta_de_cores": [{ "cor_principal": "Rosa", "etapa": "jornada pre-chaves", "descricao": "", "cores_hex_aproximadas": [] }] }
        """;
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_jornada_cliente.json"), jornada);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paleta);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_entrega_de_chaves.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var banner = loader.SelecionarBanner("mrv", "pre-chaves", "vistoria", new List<string> { "vistoria", "entrega-chaves" }, "quero agendar a vistoria");

        banner.Should().NotBeNull();
        banner!.Nome.Should().Be("agendar_vistoria");
    }

    [Fact]
    public async Task SelecionarBanner_WithTextoDisambiguation_ShouldPickCorrectVariant()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        var jornada = """
        { "Acompanhar": { "Jornada-pre-chaves": { "1": "Vistoria Antecipada" } } }
        """;
        var paleta = """
        { "paleta_de_cores": [{ "cor_principal": "Rosa", "etapa": "jornada pre-chaves", "descricao": "", "cores_hex_aproximadas": [] }] }
        """;
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_jornada_cliente.json"), jornada);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paleta);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_vistoria_info.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var banner = loader.SelecionarBanner("mrv", "pre-chaves", "vistoria", new List<string> { "vistoria" }, "informacoes sobre a vistoria");

        banner.Should().NotBeNull();
        banner!.Nome.Should().Be("vistoria_info");
    }

    [Fact]
    public async Task SelecionarBanner_WithNoMatch_ShouldReturnNull()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_financiamento_banco.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var banner = loader.SelecionarBanner("mrv", "pos-chaves", null, new List<string> { "assistencia" }, "pintura da fachada");

        banner.Should().BeNull();
    }

    [Fact]
    public async Task SelecionarBanner_WithNoBanners_ShouldReturnNull()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "MRV_logo.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var banner = loader.SelecionarBanner("mrv", "pre-chaves", null, null, "vistoria");

        banner.Should().BeNull();
    }

    [Fact]
    public async Task SelecionarBanner_WithUnknownClient_ShouldReturnNull()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var banner = loader.SelecionarBanner("unknown", null, null, null, "texto");

        banner.Should().BeNull();
    }
}
