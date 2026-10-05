namespace DemoAgencia.Worker.Agentes;

public record AgenteDefinicao(
    string Nome,
    string Modelo,
    double Temperatura,
    int MaxTokens,
    string Persona);
