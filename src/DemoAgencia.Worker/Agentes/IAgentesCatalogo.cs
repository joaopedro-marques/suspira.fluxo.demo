namespace DemoAgencia.Worker.Agentes;

public interface IAgentesCatalogo
{
    AgenteDefinicao? ObterPorComando(string comando);
    AgenteDefinicao? ObterPorNome(string nome);
    AgenteDefinicao? ObterPorPapel(string papel);
    IReadOnlyCollection<AgenteDefinicao> ListarAgentes();
    IReadOnlyCollection<AgenteDefinicao> ListarAgentesProducao();
}
