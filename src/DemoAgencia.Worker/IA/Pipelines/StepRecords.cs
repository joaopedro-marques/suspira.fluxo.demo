using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines;

public record MarcaEmail(
    string? TomDeVoz,
    string? Cores,
    byte[]? LogoBytes,
    string? LogoExtensao);

public record CopyEmailSlots(
    string Assunto,
    string Preheader,
    string Titulo,
    string Saudacao,
    string Corpo,
    string CtaTexto,
    string CtaLink,
    string Rodape);
