namespace DemoAgencia.Worker.IA.Router;

public record ImagemBrief(string Papel, string Descricao);

public record Brief(
    string Canal,
    string? Objetivo,
    string? Publico,
    string? Oferta,
    string? Tom,
    string? Link,
    List<string> Restricoes,
    List<ImagemBrief> Imagens,
    string? EtapaJornada = null,
    string? SubJornada = null);
