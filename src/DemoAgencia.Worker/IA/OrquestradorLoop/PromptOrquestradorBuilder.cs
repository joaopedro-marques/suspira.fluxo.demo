using DemoAgencia.Worker.Agentes;
using DemoAgencia.Worker.IA.Ferramentas;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public class PromptOrquestradorBuilder
{
    private readonly IAgentesCatalogo _agentesCatalogo;
    private readonly FerramentaRegistry _ferramentaRegistry;

    public PromptOrquestradorBuilder(IAgentesCatalogo agentesCatalogo, FerramentaRegistry ferramentaRegistry)
    {
        _agentesCatalogo = agentesCatalogo;
        _ferramentaRegistry = ferramentaRegistry;
    }

    public string Build(AgenteDefinicao orquestrador)
    {
        var agentes = _agentesCatalogo.ListarAgentesProducao();
        var ferramentas = _ferramentaRegistry.Listar();

        var prompt = orquestrador.Persona;
        prompt += "\n\n## Agentes disponiveis:\n";
        foreach (var a in agentes)
        {
            prompt += $"- {a.Nome}: {a.Descricao}\n";
        }

        if (ferramentas.Any())
        {
            prompt += "\n## Ferramentas disponiveis:\n";
            foreach (var f in ferramentas)
            {
                prompt += $"- {f.Nome}: {f.Descricao}\n";
            }
        }

        prompt += "\n## Protocolo:\nResponda apenas com JSON: {\"acao\": \"responder_direto\"|\"fora_contexto\"|\"chamar_agente\"|\"chamar_ferramenta\"|\"finalizar\", ...}";

        return prompt;
    }
}
