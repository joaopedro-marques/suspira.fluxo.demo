using DemoAgencia.Worker.IA;

namespace DemoAgencia.Worker.IA.Pipelines;

public record MarcaEmail(
    byte[]? LogoBytes,
    string? LogoExtensao);

public record CopyEmailSlots(
    string Assunto,
    string Preheader,
    string Titulo,
    string Corpo,
    string CtaTexto,
    string CtaLink);
