namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Fase do dialogo pos-entrega. A entrega so aceita ajustes enquanto nao estiver aprovada;
/// depois de aprovada, um novo texto livre volta a ser tratado como novo pedido pelo router.
/// </summary>
public enum FaseAjuste
{
    /// <summary>Entrega aguardando aprovacao ou pedido de ajuste.</summary>
    AposEntrega,

    /// <summary>O cliente pediu ajuste e o bot aguarda a descricao do que deve mudar.</summary>
    AguardandoFeedback,

    /// <summary>O refinador ja classificou o feedback e o bot aguarda confirmacao do plano.</summary>
    AguardandoConfirmacao
}
