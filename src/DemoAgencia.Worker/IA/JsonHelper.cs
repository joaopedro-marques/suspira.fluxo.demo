namespace DemoAgencia.Worker.IA;

public static class JsonHelper
{
    public static string? ExtrairJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var primeiro = texto.IndexOf('{');
        var ultimo = texto.LastIndexOf('}');

        if (primeiro >= 0 && ultimo > primeiro)
        {
            return texto.Substring(primeiro, ultimo - primeiro + 1);
        }

        return null;
    }
}
