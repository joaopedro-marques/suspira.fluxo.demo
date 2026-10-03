namespace DemoAgencia.Worker.Telegram;

public static class TelegramMessageSplitter
{
    public const int LimiteTelegram = 4000;

    public static List<string> Dividir(string texto, int limite = LimiteTelegram)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return [texto];
        }

        if (texto.Length <= limite)
        {
            return [texto];
        }

        var partes = new List<string>();
        var inicio = 0;

        while (inicio < texto.Length)
        {
            var fim = Math.Min(inicio + limite, texto.Length);

            if (fim < texto.Length)
            {
                var quebraLinha = texto.LastIndexOf('\n', fim - 1, fim - inicio);
                if (quebraLinha > inicio)
                {
                    fim = quebraLinha + 1;
                }
            }

            partes.Add(texto.Substring(inicio, fim - inicio).TrimEnd());
            inicio = fim;
        }

        return partes;
    }
}
