namespace DemoAgencia.Worker.IA.Ajustes;

/// <summary>
/// Callbacks dos botoes pos-entrega e do fluxo de ajuste.
/// </summary>
public static class AjusteCallbacks
{
    public const string PrefixoEntrega = "entrega";
    public const string PrefixoAjuste = "ajuste";

    public const string AprovarEntrega = "entrega|aprovar";
    public const string PedirAjustes = "entrega|ajustar";
    public const string AplicarAjuste = "ajuste|aplicar";
    public const string CancelarAjuste = "ajuste|cancelar";

    public static bool EhCallback(string? callbackData, out string acao)
    {
        acao = string.Empty;

        if (string.IsNullOrWhiteSpace(callbackData))
            return false;

        var partes = callbackData.Split('|');
        if (partes.Length != 2)
            return false;

        var fluxo = partes[0];
        if (!fluxo.Equals(PrefixoEntrega, StringComparison.OrdinalIgnoreCase) &&
            !fluxo.Equals(PrefixoAjuste, StringComparison.OrdinalIgnoreCase))
            return false;

        acao = $"{fluxo.ToLowerInvariant()}|{partes[1].ToLowerInvariant()}";
        return true;
    }
}
