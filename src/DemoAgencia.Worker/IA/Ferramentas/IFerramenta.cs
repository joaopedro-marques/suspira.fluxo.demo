using System.Text.Json;

namespace DemoAgencia.Worker.IA.Ferramentas;

public interface IFerramenta
{
    string Nome { get; }
    string Descricao { get; }
    Task<string> ExecutarAsync(OrquestradorLoop.LoopContext context, JsonElement parametros, CancellationToken ct);
}
