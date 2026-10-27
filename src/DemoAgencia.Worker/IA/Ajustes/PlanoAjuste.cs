namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Plano de ajuste pontual gerado pelo refinador a partir do feedback do cliente.
/// </summary>
public sealed record PlanoAjuste(
    string StepAlvo,
    string Instrucoes,
    string FeedbackCliente,
    string? TemplateId = null);
