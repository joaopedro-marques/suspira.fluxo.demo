using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA;
using DemoAgencia.Worker.Referencias;

namespace DemoAgencia.Worker.IA.Pipelines.Email;

public class PipelineEmail
{
    private readonly IServicoChat _servicoChat;
    private readonly IGeradorImagem _geradorImagem;
    private readonly IReferenciasCliente _referencias;
    private readonly IAnalisadorImagem _analisadorImagem;
    private readonly IAgentesCatalogo _catalogo;
    private readonly ITemplateCatalogo _templateCatalogo;
    private readonly IBannerDescricaoCache _bannerCache;
    private readonly IIconDescricaoCache _iconCache;
    private readonly string _heroSectionTemplate;
    private readonly string _bannerSectionTemplate;

    public PipelineEmail(
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem,
        IAgentesCatalogo catalogo,
        ITemplateCatalogo templateCatalogo,
        IBannerDescricaoCache bannerCache,
        IIconDescricaoCache iconCache,
        string heroSectionTemplate,
        string bannerSectionTemplate)
    {
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _catalogo = catalogo;
        _templateCatalogo = templateCatalogo;
        _bannerCache = bannerCache;
        _iconCache = iconCache;
        _heroSectionTemplate = heroSectionTemplate;
        _bannerSectionTemplate = bannerSectionTemplate;
    }

    public IReadOnlyList<IPipelineStep> CriarSteps()
    {
        var steps = new List<IPipelineStep>
        {
            new StepEstrategiaEmail(_referencias, _templateCatalogo),
            new StepMarcaEmail(_referencias),
            new StepCopyEmail(_catalogo.Obter("redator"), _servicoChat),
            new StepDiagramacaoEmail(_catalogo.Obter("diagramador"), _servicoChat, _referencias, _iconCache),
            new StepImagemHero(_catalogo.Obter("hero"), _catalogo.Obter("curador"), _servicoChat, _geradorImagem, _referencias, _analisadorImagem, _templateCatalogo, _bannerCache, _iconCache),
            new StepTemplateEmail(_templateCatalogo, _heroSectionTemplate, _bannerSectionTemplate),
            new StepAssetsEmail(_referencias),
            new StepQaEmail(_catalogo.Obter("qa"), _servicoChat)
        };

        return steps;
    }
}
