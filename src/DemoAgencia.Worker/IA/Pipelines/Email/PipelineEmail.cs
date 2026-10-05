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
    private readonly string _templateEmail;
    private readonly string _heroSectionTemplate;

    public PipelineEmail(
        IServicoChat servicoChat,
        IGeradorImagem geradorImagem,
        IReferenciasCliente referencias,
        IAnalisadorImagem analisadorImagem,
        IAgentesCatalogo catalogo,
        string templateEmail,
        string heroSectionTemplate)
    {
        _servicoChat = servicoChat;
        _geradorImagem = geradorImagem;
        _referencias = referencias;
        _analisadorImagem = analisadorImagem;
        _catalogo = catalogo;
        _templateEmail = templateEmail;
        _heroSectionTemplate = heroSectionTemplate;
    }

    public IReadOnlyList<IPipelineStep> CriarSteps()
    {
        var steps = new List<IPipelineStep>
        {
            new StepEstrategiaEmail(_referencias),
            new StepMarcaEmail(_referencias),
            new StepCopyEmail(_catalogo.Obter("redator"), _servicoChat),
            new StepImagemHero(_catalogo.Obter("hero"), _servicoChat, _geradorImagem, _referencias, _analisadorImagem),
            new StepTemplateEmail(_templateEmail, _heroSectionTemplate),
            new StepQaEmail(_catalogo.Obter("qa"), _servicoChat)
        };

        return steps;
    }
}
