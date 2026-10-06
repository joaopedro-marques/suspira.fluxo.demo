namespace DemoAgencia.Worker.Referencias;

public interface ITemplateCatalogo : IHostedService
{
    string Selecionar(string? cliente, string? fase, string? subJornada, string? texto);
    string? Obter(string id);
    string? Default { get; }
    IReadOnlyList<string> ObterSubJornadas(string id);
}
