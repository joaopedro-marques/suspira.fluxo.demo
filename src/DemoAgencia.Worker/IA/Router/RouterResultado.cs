namespace DemoAgencia.Worker.IA.Router;

public record RouterResultado(
    string Tipo,
    string? Resposta,
    List<string> Perguntas,
    string? Cliente,
    Brief? Brief);
