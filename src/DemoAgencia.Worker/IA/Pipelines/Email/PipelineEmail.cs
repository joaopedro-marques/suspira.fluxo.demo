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
    private readonly string _heroSectionTemplate;

    public PipelineEmail(
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem,
        IAgentesCatalogo catalogo,
        ITemplateCatalogo templateCatalogo,
        string heroSectionTemplate)
    {
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _catalogo = catalogo;
        _templateCatalogo = templateCatalogo;
        _heroSectionTemplate = heroSectionTemplate;
    }

    public IReadOnlyList<IPipelineStep> CriarSteps()
    {
        var steps = new List<IPipelineStep>
        {
            new StepEstrategiaEmail(_referencias, _templateCatalogo),
            new StepMarcaEmail(_referencias),
            new StepCopyEmail(_catalogo.Obter("redator"), _servicoChat),
            new StepImagemHero(_catalogo.Obter("hero"), _servicoChat, _geradorImagem, _referencias, _analisadorImagem),
            new StepTemplateEmail(_templateCatalogo, _heroSectionTemplate),
            new StepQaEmail(_catalogo.Obter("qa"), _servicoChat)
        };

        return steps;
    }
}
