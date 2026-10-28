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

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_paleta_de_cores.json"), paleta);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_temas_jornadas.json"), temas);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_jornada_cliente.json"), jornada);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_mapa_emocional_jornada.json"), mapa);

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
    public async Task ObterEstrategia_WithFasesJson_ShouldLoadFasesConfig()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        
        var fasesJson = """
        {
          "fases": {
            "pos-compra": {
              "sinonimos": ["pos-compras", "pós-compra", "pós-compras"]
            },
            "pre-chaves": {
              "sinonimos": ["pré-chaves"]
            },
            "pos-chaves": {
              "sinonimos": ["pós-chaves", "morador"]
            }
          }
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_fases.json"), fasesJson);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia.Should().NotBeNull();
        estrategia!.ConfiguracaoFases.Should().NotBeNull();
        estrategia.ConfiguracaoFases!.Fases.Should().ContainKey("pos-compra");
        estrategia.ConfiguracaoFases.Fases.Should().ContainKey("pre-chaves");
        estrategia.ConfiguracaoFases.Fases.Should().ContainKey("pos-chaves");
        
        var posCompra = estrategia.ConfiguracaoFases.Fases["pos-compra"];
        posCompra.Sinonimos.Should().Contain("pos-compras");
        posCompra.Sinonimos.Should().Contain("pós-compra");
        
        estrategia.NormalizarFase("pós-compra").Should().Be("pos-compra");
        estrategia.NormalizarFase("pos-compras").Should().Be("pos-compra");
        estrategia.NormalizarFase("pré-chaves").Should().Be("pre-chaves");
        estrategia.NormalizarFase("morador").Should().Be("pos-chaves");
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

    [Fact]
    public async Task SelecionarBannersRanked_ShouldReturnMultipleByScore()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_vistoria_info.png"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_financiamento_banco.png"), new byte[] { 3 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked("mrv", "pre-chaves", "vistoria", new List<string> { "vistoria" }, "vistoria", 5);

        ranked.Should().HaveCountGreaterThanOrEqualTo(2);
        ranked.First().Nome.Should().Be("agendar_vistoria");
    }

    [Fact]
    public async Task SelecionarBannersRanked_ShouldRespectMax()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_vistoria_info.png"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_financiamento_banco.png"), new byte[] { 3 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked("mrv", "pre-chaves", "vistoria", null, "vistoria info", 1);

        ranked.Should().HaveCount(1);
    }

    [Fact]
    public async Task SelecionarBannersRanked_ShouldOnlyIncludeScoreAboveZero()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_financiamento_banco.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked("mrv", "pos-chaves", null, null, "pintura da fachada", 5);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task SelecionarBannersRanked_WithUnknownClient_ShouldReturnEmpty()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked("unknown", null, null, null, "texto", 3);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task SelecionarBannersRanked_WithMaxZero_ShouldReturnEmpty()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 1 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked("mrv", null, null, null, "vistoria", 0);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task SelecionarBannersRanked_WithTemaMatchingBannerName_ShouldReturnOnlyThemeBanners()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_juros_fase_de_obra.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_solicitar_assitencia_tecnica.png"), new byte[] { 3 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked(
            "mrv", "pos-compra", "vistoria", null, "vistoria", 5,
            tema: "Juros de Obra");

        ranked.Should().HaveCount(1);
        ranked[0].Nome.Should().Be("juros_fase_de_obra");
    }

    [Fact]
    public async Task SelecionarBannersRanked_WithTemaNotMatchingAnyName_ShouldFallbackToScoreBased()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_juros_fase_de_obra.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked(
            "mrv", "pos-chaves", null, null, null, 5,
            tema: "Sustentabilidade");

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task SelecionarBannersRanked_WithTemaNotMatching_ShouldNotFallbackToScoreBased()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);

        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_juros_fase_de_obra.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_agendar_vistoria.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var ranked = loader.SelecionarBannersRanked(
            "mrv", "pre-chaves", "vistoria", null, "vistoria", 5,
            tema: "Sustentabilidade");

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldIgnoreDescJsonSidecars()
    {
        var bannersDir = Path.Combine(_tempDir, "imagens", "banners");
        Directory.CreateDirectory(bannersDir);
        await File.WriteAllBytesAsync(Path.Combine(bannersDir, "MRV_test.png"), new byte[] { 1 });
        await File.WriteAllTextAsync(Path.Combine(bannersDir, "MRV_test.png.desc.json"), """{"descricao_geral":"cached"}""");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReferenciasTexto("mrv").Should().BeEmpty();
        loader.ListarAssets("mrv").Should().HaveCount(1);
    }

    [Fact]
    public async Task ListarAssets_WithIconsDirectory_ShouldClassifyAsIcon()
    {
        var iconsDir = Path.Combine(_tempDir, "imagens", "icons");
        Directory.CreateDirectory(iconsDir);

        await File.WriteAllBytesAsync(Path.Combine(iconsDir, "MRV_casa.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(iconsDir, "MRV_telefone.png"), new byte[] { 2 });

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var assets = loader.ListarAssets("mrv");

        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Icon && a.Nome == "casa");
        assets.Should().ContainSingle(a => a.Tipo == TipoAsset.Icon && a.Nome == "telefone");
    }

    [Fact]
    public async Task CarregarFooters_WithDefaultFooter_ShouldLoad()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer.html"), "<footer>MRV Default</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var footer = loader.ObterFooterHtml("mrv");

        footer.Should().Be("<footer>MRV Default</footer>");
    }

    [Fact]
    public async Task CarregarFooters_WithNamedFooter_ShouldLoad()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer_mrveco.html"), "<footer>MRV&CO</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var footer = loader.ObterFooterHtml("mrv", "mrveco");

        footer.Should().Be("<footer>MRV&CO</footer>");
    }

    [Fact]
    public async Task CarregarFooters_WithMultipleFooters_ShouldLoadAll()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer.html"), "<footer>Default</footer>");
        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer_mrveco.html"), "<footer>MRV&CO</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var defaultFooter = loader.ObterFooterHtml("mrv");
        var namedFooter = loader.ObterFooterHtml("mrv", "mrveco");

        defaultFooter.Should().Be("<footer>Default</footer>");
        namedFooter.Should().Be("<footer>MRV&CO</footer>");
    }

    [Fact]
    public async Task ObterFooterHtml_WithNonExistentClient_ShouldReturnNull()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer.html"), "<footer>MRV</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var footer = loader.ObterFooterHtml("acme");

        footer.Should().BeNull();
    }

    [Fact]
    public async Task ObterFooterHtml_WithNonExistentName_ShouldReturnNull()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "MRV_footer.html"), "<footer>Default</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var footer = loader.ObterFooterHtml("mrv", "nonexistent");

        footer.Should().BeNull();
    }

    [Fact]
    public async Task ListarClientes_WithFooterOnly_ShouldIncludeClient()
    {
        var footerDir = Path.Combine(_tempDir, "footer");
        Directory.CreateDirectory(footerDir);

        await File.WriteAllTextAsync(Path.Combine(footerDir, "ACME_footer.html"), "<footer>ACME</footer>");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var clientes = loader.ListarClientes();

        clientes.Should().Contain("acme");
    }

    [Fact]
    public async Task ObterEstrategia_WithReguaJson_ShouldLoadRegua()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);

        var regua = """
        {
          "regua": {
            "etapas": [
              "Assinatura do Contrato",
              "Financiamento com o banco",
              "Visita a Obra",
              "Vistoria",
              "Entrega das chaves"
            ],
            "etapa_destacada_por_fase": {
              "pos-compra": "Financiamento com o banco",
              "pre-chaves": "Vistoria",
              "pos-chaves": "Entrega das chaves"
            },
            "estilo": {
              "concluida": "line WHITE fill on the progress line segment",
              "atual": "solid YELLOW highlight",
              "futura": "outline only, no fill",
              "diretrizes": [
                "Render the stage labels in Portuguese EXACTLY as written",
                "Design: smooth drop shadows, NO gradients"
              ]
            }
          }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_regua_jornada.json"), regua);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia.Should().NotBeNull();
        estrategia!.Regua.Should().NotBeNull();
        estrategia.Regua!.Etapas.Should().HaveCount(5);
        estrategia.Regua.Etapas.Should().Contain("Assinatura do Contrato");
        estrategia.Regua.Etapas.Should().Contain("Vistoria");
        estrategia.Regua.EtapaDestacadaPorFase.Should().ContainKey("pos-compra");
        estrategia.Regua.EtapaDestacadaPorFase["pos-compra"].Should().Be("Financiamento com o banco");
        estrategia.Regua.EtapaDestacadaPorFase["pre-chaves"].Should().Be("Vistoria");
        estrategia.Regua.Estilo.Should().NotBeNull();
        estrategia.Regua.Estilo.Concluida.Should().Contain("WHITE");
        estrategia.Regua.Estilo.Atual.Should().Contain("YELLOW");
        estrategia.Regua.Estilo.Diretrizes.Should().HaveCount(2);
    }

    [Fact]
    public async Task ObterEstrategia_WithoutReguaJson_ShouldHaveNullRegua()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("acme");

        estrategia.Should().BeNull();
    }

    [Fact]
    public async Task ObterEstrategia_WithReguaEstiloHtml_ShouldLoadHtmlColors()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);

        var regua = """
        {
          "regua": {
            "etapas": ["Assinatura do Contrato", "Vistoria"],
            "etapa_destacada_por_fase": { "pre-chaves": "Vistoria" },
            "estilo_html": {
              "cor_concluida": "#EEEEEE",
              "cor_atual": "#FFCC00",
              "cor_futura": "rgba(255, 255, 255, 0.2)",
              "cor_label": "#F5F5F5",
              "cor_label_atual": "#FFAA00"
            }
          }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_regua_jornada.json"), regua);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia.Should().NotBeNull();
        estrategia!.Regua.Should().NotBeNull();
        estrategia.Regua!.EstiloHtml.CorConcluida.Should().Be("#EEEEEE");
        estrategia.Regua.EstiloHtml.CorAtual.Should().Be("#FFCC00");
        estrategia.Regua.EstiloHtml.CorFutura.Should().Be("rgba(255, 255, 255, 0.2)");
        estrategia.Regua.EstiloHtml.CorLabel.Should().Be("#F5F5F5");
        estrategia.Regua.EstiloHtml.CorLabelAtual.Should().Be("#FFAA00");
    }

    [Fact]
    public async Task ObterEstrategia_WithoutReguaEstiloHtml_ShouldUseDefaultColors()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);

        var regua = """
        {
          "regua": {
            "etapas": ["Assinatura do Contrato", "Vistoria"],
            "etapa_destacada_por_fase": { "pre-chaves": "Vistoria" }
          }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "MRV_regua_jornada.json"), regua);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("mrv");

        estrategia!.Regua!.EstiloHtml.CorConcluida.Should().Be("#FFFFFF");
        estrategia.Regua.EstiloHtml.CorAtual.Should().Be("#FFDD02");
    }

    [Fact]
    public async Task ObterHeaderSectionTemplate_WhenComponentExists_ShouldLoad()
    {
        var componentsDir = Path.Combine(_tempDir, "components");
        Directory.CreateDirectory(componentsDir);

        const string header = "<tr><td>{{header_bg}}{{regua_html}}</td></tr>";
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "header_section.html"), header);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterHeaderSectionTemplate().Should().Be(header);
    }

    [Fact]
    public async Task ObterHeaderSectionTemplate_WhenComponentMissing_ShouldReturnNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterHeaderSectionTemplate().Should().BeNull();
    }

    [Fact]
    public async Task ObterHeaderSectionTemplate_PorCliente_TemPrecedenciaSobreOGlobal()
    {
        var componentsDir = Path.Combine(_tempDir, "components");
        Directory.CreateDirectory(componentsDir);

        const string global = "<tr><td>global</td></tr>";
        const string mrv = "<tr><td>mrv</td></tr>";
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "header_section.html"), global);
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "header_section_mrv.html"), mrv);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterHeaderSectionTemplate("mrv").Should().Be(mrv);
        loader.ObterHeaderSectionTemplate("acme").Should().Be(global);
        loader.ObterHeaderSectionTemplate().Should().Be(global);
    }

    [Fact]
    public async Task ObterReguaSectionTemplate_WhenComponentExists_ShouldLoad()
    {
        var componentsDir = Path.Combine(_tempDir, "components");
        Directory.CreateDirectory(componentsDir);

        const string regua = "<table><tr>{{regua_segmentos}}</tr><tr>{{regua_labels}}</tr></table>";
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "regua_section.html"), regua);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReguaSectionTemplate().Should().Be(regua);
    }

    [Fact]
    public async Task ObterReguaSectionTemplate_WhenComponentMissing_ShouldReturnNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "acme_marca.json"), "{}");

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReguaSectionTemplate().Should().BeNull();
    }

    [Fact]
    public async Task ObterReguaSectionTemplate_PorCliente_TemPrecedenciaSobreOGlobal()
    {
        var componentsDir = Path.Combine(_tempDir, "components");
        Directory.CreateDirectory(componentsDir);

        const string global = "<table><tr><td>global</td></tr></table>";
        const string mrv = "<table><tr><td>mrv</td></tr></table>";
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "regua_section.html"), global);
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "regua_section_mrv.html"), mrv);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        loader.ObterReguaSectionTemplate("mrv").Should().Be(mrv);
        loader.ObterReguaSectionTemplate("acme").Should().Be(global);
        loader.ObterReguaSectionTemplate().Should().Be(global);
    }

    [Fact]
    public async Task ObterEstrategia_ComHeaderJson_DeveCarregarConfiguracaoDoHeader()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "acme_fases.json"), """
            { "fases": { "pos-compra": { "sinonimos": ["pos compra", "pos-compra"] } } }
            """);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "acme_header.json"), """
            {
              "colunas": ["hero", "titulo"],
              "sortear_ordem": false,
              "requer_hero": false,
              "clip_path_padrao": "clip-path: polygon(0 0, 1px 0);",
              "gradiente_angulo": "90deg"
            }
            """);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("acme");

        estrategia.Should().NotBeNull();
        estrategia!.Header.Should().NotBeNull();
        estrategia.Header!.Colunas.Should().Equal("hero", "titulo");
        estrategia.Header.SortearOrdem.Should().BeFalse();
        estrategia.Header.RequerHero.Should().BeFalse();
        estrategia.Header.ClipPathPadrao.Should().Be("clip-path: polygon(0 0, 1px 0);");
        estrategia.Header.GradienteAngulo.Should().Be("90deg");
    }

    [Fact]
    public async Task ObterEstrategia_SemHeaderJson_UsaPadroesDoHeader()
    {
        var estrategiaDir = Path.Combine(_tempDir, "estrategia");
        Directory.CreateDirectory(estrategiaDir);
        await File.WriteAllTextAsync(Path.Combine(estrategiaDir, "acme_fases.json"), """
            { "fases": { "pos-compra": { "sinonimos": ["pos compra", "pos-compra"] } } }
            """);

        var loader = new ReferenciaClienteLoader(_loggerMock.Object, _configuration, _tempDir);
        await loader.StartAsync(CancellationToken.None);

        var estrategia = loader.ObterEstrategia("acme")!;

        estrategia.Header.Should().BeNull();
        HeaderConfig.ColunasPadrao.Should().Equal("titulo", "hero");
    }
}
