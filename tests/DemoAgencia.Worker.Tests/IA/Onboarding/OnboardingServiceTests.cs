using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.IA.Onboarding;
using DemoAgencia.Worker.IA.Router;
using DemoAgencia.Worker.Referencias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Onboarding;

public class OnboardingServiceTests
{
    private readonly OnboardingService _service;
    private readonly OnboardingStore _store;
    private readonly Mock<IReferenciasCliente> _referencias;

    public OnboardingServiceTests()
    {
        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(new PreFlightOptions { TimeoutMinutosPendencia = 15 });
        var storeLogger = new Mock<ILogger<OnboardingStore>>();
        _store = new OnboardingStore(options, timeProvider, storeLogger.Object);
        _referencias = new Mock<IReferenciasCliente>();
        var serviceLogger = new Mock<ILogger<OnboardingService>>();
        _service = new OnboardingService(_store, _referencias.Object, serviceLogger.Object);
    }

    #region Iniciar

    [Fact]
    public void Iniciar_WithNullCliente_ShouldReturnClienteQuestion()
    {
        _referencias.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv", "outro" });
        _referencias.Setup(r => r.ObterEstrategia(It.IsAny<string>()))
            .Returns<string>(c => c == "mrv" ? CriarEstrategiaCompleta() : null);

        var pergunta = _service.Iniciar(123, "", "mensagem original");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        pergunta.Opcoes.Should().Contain(o => o.CallbackData.Contains("mrv"));
        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Cliente);
    }

    [Fact]
    public void Responder_ClienteValido_ShouldAdvanceToEtapa()
    {
        SetupEstrategiaCompleta();
        _referencias.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv" });
        _service.Iniciar(123, "", "mensagem original");

        var pergunta = _service.Responder(123, "onb|cliente|mrv");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        var state = _store.Obter(123);
        state!.Cliente.Should().Be("mrv");
        state.PassoAtual.Should().Be(OnboardingPasso.Etapa);
    }

    [Fact]
    public void Responder_ClienteInvalido_ShouldReaskCliente()
    {
        _referencias.Setup(r => r.ListarClientes()).Returns(new List<string> { "mrv", "outro_cliente" });
        _referencias.Setup(r => r.ObterEstrategia(It.IsAny<string>()))
            .Returns<string>(c => c == "mrv" ? CriarEstrategiaCompleta() : null);
        _service.Iniciar(123, "", "mensagem original");

        var pergunta = _service.Responder(123, "onb|cliente|outro_cliente");

        pergunta.Should().NotBeNull();
        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Cliente);
        state.Cliente.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Iniciar_WithValidClient_ShouldReturnEtapaQuestion()
    {
        SetupEstrategiaCompleta();

        var pergunta = _service.Iniciar(123, "mrv", "criar email");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        pergunta.Opcoes.Should().HaveCount(3);
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|etapa|pos-compra");
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|etapa|pre-chaves");
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|etapa|pos-chaves");
    }

    [Fact]
    public void Iniciar_ShouldCreateStateInStore()
    {
        SetupEstrategiaCompleta();

        _service.Iniciar(123, "mrv", "criar email");

        var state = _store.Obter(123);
        state.Should().NotBeNull();
        state!.ChatId.Should().Be(123);
        state.Cliente.Should().Be("mrv");
        state.PassoAtual.Should().Be(OnboardingPasso.Etapa);
    }

    [Fact]
    public void Iniciar_WithoutStrategy_ShouldReturnNull()
    {
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns((EstrategiaCliente?)null);

        var pergunta = _service.Iniciar(123, "mrv", "criar email");

        pergunta.Should().BeNull();
    }

    #endregion

    #region Responder — transições básicas

    [Fact]
    public void Responder_Etapa_ShouldAdvanceToMarco()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");

        var pergunta = _service.Responder(123, "onb|etapa|pos-compra");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        pergunta.Opcoes.Should().HaveCountGreaterThan(0);
        pergunta.Opcoes.All(o => o.CallbackData.StartsWith("onb|marco|")).Should().BeTrue();

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
        state.PassoAtual.Should().Be(OnboardingPasso.Marco);
    }

    [Fact]
    public void Responder_Marco_ShouldAdvanceToTema()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");

        var pergunta = _service.Responder(123, "onb|marco|Pós Financiamento");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        pergunta.Opcoes.All(o => o.CallbackData.StartsWith("onb|tema|")).Should().BeTrue();

        var state = _store.Obter(123);
        state!.Marco.Should().Be("Pós Financiamento");
        state.PassoAtual.Should().Be(OnboardingPasso.Tema);
    }

    [Fact]
    public void Responder_Tema_ShouldAdvanceToObjetivo()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");

        var pergunta = _service.Responder(123, "onb|tema|Boas vindas");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
        pergunta.Opcoes.Should().HaveCount(3);
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|objetivo|vender");
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|objetivo|engajar");
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|objetivo|informar");

        var state = _store.Obter(123);
        state!.Tema.Should().Be("Boas vindas");
        state.PassoAtual.Should().Be(OnboardingPasso.Objetivo);
    }

    [Fact]
    public void Responder_Objetivo_ShouldAdvanceToOferta()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");

        var pergunta = _service.Responder(123, "onb|objetivo|vender");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.TextoLivre);

        var state = _store.Obter(123);
        state!.Objetivo.Should().Be("vender");
        state.PassoAtual.Should().Be(OnboardingPasso.Oferta);
    }

    [Fact]
    public void Responder_SemLink_ShouldAdvanceToMateriais()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.ResponderTexto(123, "Oferta teste");
        _service.ResponderTexto(123, "Publico teste");

        var pergunta = _service.Responder(123, "onb|sem-link");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Coleta);

        var state = _store.Obter(123);
        state!.CtaLink.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.Materiais);
    }

    [Fact]
    public void Responder_MateriaisPular_ShouldAdvanceToConfirmacao()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarAteMateriais(123);

        var pergunta = _service.Responder(123, "onb|pular");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Confirmacao);
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|confirmar");
        pergunta.Opcoes.Select(o => o.CallbackData).Should().Contain("onb|recomecar");

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Confirmacao);
    }

    [Fact]
    public void Responder_InvalidCallback_ShouldReturnNull()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");

        var pergunta = _service.Responder(123, "invalid_callback");

        pergunta.Should().BeNull();
    }

    [Fact]
    public void Responder_WrongStepCallback_ShouldReturnNull()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");

        var pergunta = _service.Responder(123, "onb|marco|Pós Financiamento");

        pergunta.Should().BeNull();
    }

    #endregion

    #region CtaLink texto livre

    [Fact]
    public void Responder_CtaLinkComTexto_ShouldSetLinkAndAdvanceToMateriais()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.ResponderTexto(123, "Oferta teste");
        _service.ResponderTexto(123, "Publico teste");

        var pergunta = _service.ResponderTexto(123, "https://mrv.com.br/promo");

        pergunta.Should().NotBeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Coleta);

        var state = _store.Obter(123);
        state!.CtaLink.Should().Be("https://mrv.com.br/promo");
        state.PassoAtual.Should().Be(OnboardingPasso.Materiais);
    }

    #endregion

    #region Materiais

    [Fact]
    public void AdicionarMaterialFoto_ShouldAddToState()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarAteMateriais(123);
        var bytes = new byte[] { 1, 2, 3 };

        _service.AdicionarMaterialFoto(123, bytes, "foto.png", "minha foto");

        var state = _store.Obter(123);
        state!.Materiais.Should().HaveCount(1);
        state.Materiais[0].Bytes.Should().BeEquivalentTo(bytes);
        state.Materiais[0].FileName.Should().Be("foto.png");
        state.Materiais[0].Caption.Should().Be("minha foto");
    }

    [Fact]
    public void AdicionarMaterialLink_ShouldAddToState()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarAteMateriais(123);

        _service.AdicionarMaterialLink(123, "https://mrv.com.br");

        var state = _store.Obter(123);
        state!.LinksExtras.Should().Contain("https://mrv.com.br");
    }

    #endregion

    #region Auto-seleção

    [Fact]
    public void Iniciar_WithSingleFase_ShouldAutoSelectEtapaAndAdvance()
    {
        SetupEstrategiaComUmaFase();

        var pergunta = _service.Iniciar(123, "mrv", "criar email");

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
        state.PassoAtual.Should().Be(OnboardingPasso.Marco);
        pergunta.Should().NotBeNull();
        pergunta!.Opcoes.All(o => o.CallbackData.StartsWith("onb|marco|")).Should().BeTrue();
    }

    [Fact]
    public void Responder_WithSingleMarco_ShouldAutoSelectAndAdvance()
    {
        SetupEstrategiaComUmMarco();
        _service.Iniciar(123, "mrv", "criar email");

        var pergunta = _service.Responder(123, "onb|etapa|pos-compra");

        var state = _store.Obter(123);
        state!.Marco.Should().Be("Pós Financiamento");
        state.PassoAtual.Should().Be(OnboardingPasso.Tema);
        pergunta!.Opcoes.All(o => o.CallbackData.StartsWith("onb|tema|")).Should().BeTrue();
    }

    [Fact]
    public void Responder_WithSingleTema_ShouldAutoSelectAndAdvance()
    {
        SetupEstrategiaComUmTema();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");

        var state1 = _store.Obter(123);
        state1!.Tema.Should().Be("Boas vindas");
        state1.PassoAtual.Should().Be(OnboardingPasso.Objetivo);

        var pergunta = _service.Responder(123, "onb|objetivo|vender");

        var state = _store.Obter(123);
        state!.Objetivo.Should().Be("vender");
        state.PassoAtual.Should().Be(OnboardingPasso.Oferta);
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.TextoLivre);
    }

    [Fact]
    public void Responder_WithNoSubJornadas_ShouldSkipMarco()
    {
        SetupEstrategiaSemSubJornadas();
        _service.Iniciar(123, "mrv", "criar email");

        var pergunta = _service.Responder(123, "onb|etapa|pos-compra");

        var state = _store.Obter(123);
        state!.Marco.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.Tema);
        pergunta!.Opcoes.All(o => o.CallbackData.StartsWith("onb|tema|")).Should().BeTrue();
    }

    [Fact]
    public void Responder_WithNoTemas_ShouldSkipTema()
    {
        SetupEstrategiaSemTemas();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");

        var state1 = _store.Obter(123);
        state1!.Tema.Should().BeNull();
        state1.PassoAtual.Should().Be(OnboardingPasso.Objetivo);

        var pergunta = _service.Responder(123, "onb|objetivo|vender");

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Oferta);
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.TextoLivre);
    }

    #endregion

    #region Pré-preenchimento

    [Fact]
    public void Iniciar_WithEtapaPrefilled_ShouldSkipEtapa()
    {
        SetupEstrategiaCompleta();
        var extraidos = new CamposExtraidos(EtapaJornada: "pos-compra");

        var pergunta = _service.Iniciar(123, "mrv", "email pos-compra", extraidos);

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
        state.PassoAtual.Should().Be(OnboardingPasso.Marco);
        pergunta!.Opcoes.All(o => o.CallbackData.StartsWith("onb|marco|")).Should().BeTrue();
    }

    [Fact]
    public void Iniciar_WithEtapaPrefilledInvalid_ShouldNotSkip()
    {
        SetupEstrategiaCompleta();
        var extraidos = new CamposExtraidos(EtapaJornada: "invalid-fase");

        var pergunta = _service.Iniciar(123, "mrv", "email", extraidos);

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.Etapa);
    }

    [Fact]
    public void Iniciar_WithMultiplePrefilled_ShouldSkipAll()
    {
        SetupEstrategiaCompleta();
        var extraidos = new CamposExtraidos(
            EtapaJornada: "pos-compra",
            Marco: "Pós Financiamento",
            Tema: "Boas vindas",
            Objetivo: "vender");

        var pergunta = _service.Iniciar(123, "mrv", "email pos-compra sobre boas vindas", extraidos);

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
        state.Marco.Should().Be("Pós Financiamento");
        state.Tema.Should().Be("Boas vindas");
        state.Objetivo.Should().Be("vender");
        state.PassoAtual.Should().Be(OnboardingPasso.Oferta);
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.TextoLivre);
    }

    #endregion

    #region Confirmar

    [Fact]
    public void Confirmar_WithAllFields_ShouldBuildCompleteBrief()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarOnboarding(123);

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Canal.Should().Be("email");
        brief.EtapaJornada.Should().Be("pos-compra");
        brief.SubJornada.Should().Be("Pós Financiamento");
        brief.Tema.Should().Be("Boas vindas");
        brief.Objetivo.Should().Be("vender");
        brief.Link.Should().Be("https://mrv.com.br");
    }

    [Fact]
    public void Confirmar_WithoutCtaLink_ShouldBuildBriefWithoutLink()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.Responder(123, "onb|sem-link");
        _service.Responder(123, "onb|pular");

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Link.Should().BeNull();
    }

    [Fact]
    public void Confirmar_ShouldRemoveStateFromStore()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarOnboarding(123);

        _service.Confirmar(123);

        _store.Obter(123).Should().BeNull();
    }

    [Fact]
    public void Confirmar_WithMateriais_ShouldIncludeInBrief()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.ResponderTexto(123, "Oferta teste");
        _service.ResponderTexto(123, "Publico teste");
        _service.ResponderTexto(123, "https://mrv.com.br");
        _service.AdicionarMaterialFoto(123, new byte[] { 1, 2, 3 }, "foto.png", "promo");
        _service.AdicionarMaterialLink(123, "https://extra.com");

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.LinksExtras.Should().Contain("https://extra.com");
    }

    [Fact]
    public void Confirmar_WithRouterBrief_ShouldPreservePublicoOfertaTom()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "Chamar clientes para a reunião condominial do Edifício Del Rey.",
            Publico: "Clientes MRV 27-45 anos, recém-casados",
            Oferta: "Reunião condominial dia 09/10 às 20h",
            Tom: "informativo e convidativo",
            Link: null,
            Restricoes: new List<string> { "nao mencionar concorrentes" },
            Imagens: new List<ImagemBrief> { new("hero", "foto do edificio") },
            EtapaJornada: "pos-chaves",
            SubJornada: null,
            Tema: "Assistência técnica");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);
        CompletarOnboarding(123);

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Publico.Should().Be("Clientes MRV 27-45 anos, recém-casados");
        brief.Oferta.Should().Be("Reunião condominial dia 09/10 às 20h");
        brief.Tom.Should().Be("informativo e convidativo");
        brief.Restricoes.Should().Contain("nao mencionar concorrentes");
        brief.Imagens.Should().HaveCount(1);
    }

    [Fact]
    public void Confirmar_WithRouterBrief_ShouldPreferRichObjetivo()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "Chamar clientes para a reunião condominial do Edifício Del Rey.",
            Publico: "publico",
            Oferta: "oferta",
            Tom: "tom",
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>());

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);
        CompletarOnboarding(123);

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Objetivo.Should().Be("Chamar clientes para a reunião condominial do Edifício Del Rey.");
    }

    [Fact]
    public void Iniciar_WithRouterBriefValidEtapa_ShouldPreFillAndSkipQuestion()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.EtapaJornada.Should().Be("pos-compra");
        state.PassoAtual.Should().NotBe(OnboardingPasso.Etapa);
    }

    [Fact]
    public void Iniciar_WithRouterBriefValidTema_ShouldPreFillAndSkipTemaQuestion()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.Tema.Should().Be("Boas vindas");
    }

    [Fact]
    public void Iniciar_WithRouterBriefObjetivo_ShouldSkipObjetivoQuestion()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "Chamar clientes para a reunião",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Oferta);
    }

    [Fact]
    public void Confirmar_SemLinkExplicit_ShouldOverrideRouterLink()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: null,
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: "https://mrv.com.br/router-link",
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>());

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.ResponderTexto(123, "Oferta teste");
        _service.ResponderTexto(123, "Publico teste");
        _service.Responder(123, "onb|sem-link");
        _service.Responder(123, "onb|pular");

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Link.Should().BeNull();
    }

    [Fact]
    public void Confirmar_UserTypesLink_ShouldOverrideRouterLink()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: null,
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: "https://mrv.com.br/router-link",
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>());

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);
        _service.Responder(123, "onb|etapa|pos-compra");
        _service.Responder(123, "onb|marco|Pós Financiamento");
        _service.Responder(123, "onb|tema|Boas vindas");
        _service.Responder(123, "onb|objetivo|vender");
        _service.ResponderTexto(123, "Oferta teste");
        _service.ResponderTexto(123, "Publico teste");
        _service.ResponderTexto(123, "https://mrv.com.br/user-link");
        _service.Responder(123, "onb|pular");

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Link.Should().Be("https://mrv.com.br/user-link");
    }

    #endregion

    #region Oferta e Público

    [Fact]
    public void Iniciar_WithRouterBriefOferta_ShouldSkipOfertaQuestion()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: "Reunião condominial dia 09/10",
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.Oferta.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.Publico);
    }

    [Fact]
    public void Iniciar_WithRouterBriefPublico_ShouldSkipPublicoQuestion()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: "Clientes MRV 27-45 anos",
            Oferta: "Reunião condominial",
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.Publico.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.CtaLink);
    }

    [Fact]
    public void ResponderTexto_WhenOferta_ShouldAdvanceToPublico()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Oferta);

        var pergunta = _service.ResponderTexto(123, "Reunião condominial do edifício Del Rey");

        pergunta.Should().NotBeNull();
        state = _store.Obter(123);
        state!.Oferta.Should().Be("Reunião condominial do edifício Del Rey");
        state.PassoAtual.Should().Be(OnboardingPasso.Publico);
    }

    [Fact]
    public void ResponderTexto_WhenPublico_ShouldAdvanceToCtaLink()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: "Reunião condominial",
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Publico);

        var pergunta = _service.ResponderTexto(123, "Clientes MRV recém-casados");

        pergunta.Should().NotBeNull();
        state = _store.Obter(123);
        state!.Publico.Should().Be("Clientes MRV recém-casados");
        state.PassoAtual.Should().Be(OnboardingPasso.CtaLink);
    }

    [Fact]
    public void Responder_PularOferta_ShouldAdvanceToPublico()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "vender",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Oferta);

        _service.Responder(123, OnboardingCallbacks.Pular);

        state = _store.Obter(123);
        state!.Oferta.Should().BeNull();
        state.PassoAtual.Should().Be(OnboardingPasso.Publico);
    }

    [Fact]
    public void Confirmar_WithOfertaPublico_ShouldIncludeInBrief()
    {
        SetupEstrategiaCompleta();
        var routerBrief = new Brief(
            Canal: "email",
            Objetivo: "engajar",
            Publico: null,
            Oferta: null,
            Tom: null,
            Link: null,
            Restricoes: new List<string>(),
            Imagens: new List<ImagemBrief>(),
            EtapaJornada: "pos-compra",
            Tema: "Boas vindas");

        _service.Iniciar(123, "mrv", "pedido original", null, routerBrief);

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Oferta);

        _service.ResponderTexto(123, "Reunião condominial");
        state = _store.Obter(123);
        state!.Oferta.Should().Be("Reunião condominial");
        state.PassoAtual.Should().Be(OnboardingPasso.Publico);

        _service.ResponderTexto(123, "Clientes MRV");
        state = _store.Obter(123);
        state!.Publico.Should().Be("Clientes MRV");
        state.PassoAtual.Should().Be(OnboardingPasso.CtaLink);

        _service.Responder(123, "onb|sem-link");
        _service.Responder(123, "onb|pular");

        var brief = _service.Confirmar(123);

        brief.Should().NotBeNull();
        brief!.Oferta.Should().Be("Reunião condominial");
        brief.Publico.Should().Be("Clientes MRV");
    }

    #endregion

    #region Recomeçar

    [Fact]
    public void Responder_Recomecar_ShouldResetStateToEtapa()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");
        CompletarOnboarding(123);

        var pergunta = _service.Responder(123, "onb|recomecar");

        var state = _store.Obter(123);
        state!.PassoAtual.Should().Be(OnboardingPasso.Etapa);
        state.EtapaJornada.Should().BeNull();
        state.Marco.Should().BeNull();
        state.Tema.Should().BeNull();
        state.Objetivo.Should().BeNull();
        pergunta!.Tipo.Should().Be(OnboardingTipoPasso.Opcoes);
    }

    #endregion

    #region Cancelar

    [Fact]
    public void Cancelar_ShouldRemoveStateFromStore()
    {
        SetupEstrategiaCompleta();
        _service.Iniciar(123, "mrv", "criar email");

        _service.Cancelar(123);

        _store.Obter(123).Should().BeNull();
    }

    #endregion

    #region Helpers

    private void SetupEstrategiaCompleta()
    {
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(CriarEstrategiaCompleta());
    }

    private void SetupEstrategiaComUmaFase()
    {
        var estrategia = new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = CriarFasePosCompra()
            }
        };
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
    }

    private void SetupEstrategiaComUmMarco()
    {
        var fasePosCompra = CriarFasePosCompra();
        fasePosCompra.SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jornada-pos-compra"] = new List<string> { "Pós Financiamento" }
        };
        var estrategia = new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = fasePosCompra,
                ["pre-chaves"] = CriarFasePreChaves(),
                ["pos-chaves"] = CriarFasePosChaves()
            }
        };
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
    }

    private void SetupEstrategiaComUmTema()
    {
        var fasePosCompra = CriarFasePosCompra();
        fasePosCompra.Temas = new List<string> { "Boas vindas" };
        var estrategia = new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = fasePosCompra,
                ["pre-chaves"] = CriarFasePreChaves(),
                ["pos-chaves"] = CriarFasePosChaves()
            }
        };
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
    }

    private void SetupEstrategiaSemSubJornadas()
    {
        var fasePosCompra = CriarFasePosCompra();
        fasePosCompra.SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var estrategia = new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = fasePosCompra,
                ["pre-chaves"] = CriarFasePreChaves(),
                ["pos-chaves"] = CriarFasePosChaves()
            }
        };
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
    }

    private void SetupEstrategiaSemTemas()
    {
        var fasePosCompra = CriarFasePosCompra();
        fasePosCompra.Temas = new List<string>();
        var estrategia = new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = fasePosCompra,
                ["pre-chaves"] = CriarFasePreChaves(),
                ["pos-chaves"] = CriarFasePosChaves()
            }
        };
        _referencias.Setup(r => r.ObterEstrategia("mrv")).Returns(estrategia);
    }

    private static EstrategiaCliente CriarEstrategiaCompleta()
    {
        return new EstrategiaCliente
        {
            Cliente = "mrv",
            Fases = new Dictionary<string, FaseEstrategia>(StringComparer.OrdinalIgnoreCase)
            {
                ["pos-compra"] = CriarFasePosCompra(),
                ["pre-chaves"] = CriarFasePreChaves(),
                ["pos-chaves"] = CriarFasePosChaves()
            }
        };
    }

    private static FaseEstrategia CriarFasePosCompra()
    {
        return new FaseEstrategia
        {
            Fase = "pos-compra",
            CorPrincipal = "Roxo",
            Temas = new List<string> { "Boas vindas", "Indicação premiada", "Financeiro" },
            SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jornada-pos-compra"] = new List<string> { "Pós Financiamento", "6 meses Pós Financiamento" }
            }
        };
    }

    private static FaseEstrategia CriarFasePreChaves()
    {
        return new FaseEstrategia
        {
            Fase = "pre-chaves",
            CorPrincipal = "Rosa",
            Temas = new List<string> { "Financeiro", "Implantação de condomínio" },
            SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jornada-pre-chaves"] = new List<string> { "Assembleia de condomínio", "Vistoria Antecipada" }
            }
        };
    }

    private static FaseEstrategia CriarFasePosChaves()
    {
        return new FaseEstrategia
        {
            Fase = "pos-chaves",
            CorPrincipal = "Amarelo",
            Temas = new List<string> { "Canais", "Assistência técnica" },
            SubJornadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jornada-do-produto-morador"] = new List<string> { "6 meses Pós chaves", "18 meses Pós chaves" }
            }
        };
    }

    private void CompletarAteMateriais(long chatId)
    {
        _service.Responder(chatId, "onb|etapa|pos-compra");
        _service.Responder(chatId, "onb|marco|Pós Financiamento");
        _service.Responder(chatId, "onb|tema|Boas vindas");
        _service.Responder(chatId, "onb|objetivo|vender");
        _service.ResponderTexto(chatId, "Oferta teste");
        _service.ResponderTexto(chatId, "Publico teste");
        _service.Responder(chatId, "onb|sem-link");
    }

    private void CompletarOnboarding(long chatId)
    {
        _service.Responder(chatId, "onb|etapa|pos-compra");
        _service.Responder(chatId, "onb|marco|Pós Financiamento");
        _service.Responder(chatId, "onb|tema|Boas vindas");
        _service.Responder(chatId, "onb|objetivo|vender");
        _service.ResponderTexto(chatId, "Oferta teste");
        _service.ResponderTexto(chatId, "Publico teste");
        _service.ResponderTexto(chatId, "https://mrv.com.br");
        _service.Responder(chatId, "onb|pular");
    }

    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void AdvanceMinutes(int minutes) => _now = _now.AddMinutes(minutes);
    }

    #endregion
}
