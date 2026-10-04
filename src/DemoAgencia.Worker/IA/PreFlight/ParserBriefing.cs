using System.Text;
using System.Text.Json;
using DemoAgencia.Worker.IA.OrquestradorLoop;

namespace DemoAgencia.Worker.IA.PreFlight;

public class ResultadoBriefing
{
    public string Briefing { get; init; } = string.Empty;
    public List<string> AssetsReservados { get; init; } = new();
    public List<string> ImagensNecessarias { get; init; } = new();
}

public record ResultadoBriefingParse(ResultadoBriefing? Resultado, string? MotivoFalha);

public static class ParserBriefing
{
    public static ResultadoBriefing? TentarExtrair(string texto)
        => TentarExtrairComDiagnostico(texto).Resultado;

    public static ResultadoBriefingParse TentarExtrairComDiagnostico(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return Falha("texto vazio");

        var json = ParserDecisao.ExtrairJson(texto);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var r = Parsear(json);
                if (r != null)
                    return new ResultadoBriefingParse(r, null);
            }
            catch (JsonException)
            {
            }
        }

        var primeiroBrace = texto.IndexOf('{');
        if (primeiroBrace >= 0)
        {
            var candidato = RepararJsonTruncado(texto.Substring(primeiroBrace));
            try
            {
                var r = Parsear(candidato);
                if (r != null)
                    return new ResultadoBriefingParse(r, null);
            }
            catch (JsonException)
            {
            }
        }

        var heuristico = ExtrairHeuristico(texto);
        if (heuristico != null)
            return new ResultadoBriefingParse(heuristico, null);

        return Falha("nenhum parser funcionou");
    }

    private static ResultadoBriefingParse Falha(string motivo) => new(null, motivo);

    private static ResultadoBriefing? Parsear(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var briefing = root.TryGetProperty("briefing", out var brEl) ? brEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(briefing))
            return null;

        var assetsReservados = ExtrairListaStrings(root, "assets_reservados");
        var imagensNecessarias = ExtrairListaStrings(root, "imagens_necessarias");

        return new ResultadoBriefing
        {
            Briefing = briefing!,
            AssetsReservados = assetsReservados,
            ImagensNecessarias = imagensNecessarias
        };
    }

    private static string RepararJsonTruncado(string json)
    {
        var sb = new StringBuilder(json.Length + 16);
        var inString = false;
        var escapeNext = false;
        var stack = new Stack<char>();

        foreach (var c in json)
        {
            sb.Append(c);
            if (escapeNext) { escapeNext = false; continue; }
            if (c == '\\' && inString) { escapeNext = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (!inString)
            {
                if (c == '{') stack.Push('}');
                else if (c == '[') stack.Push(']');
                else if ((c == '}' || c == ']') && stack.Count > 0) stack.Pop();
            }
        }

        if (inString) sb.Append('"');
        while (stack.Count > 0)
            sb.Append(stack.Pop());

        return sb.ToString();
    }

    private static ResultadoBriefing? ExtrairHeuristico(string texto)
    {
        var briefingKey = texto.IndexOf("\"briefing\"", StringComparison.Ordinal);
        if (briefingKey < 0)
            return null;

        var colon = texto.IndexOf(':', briefingKey + 10);
        if (colon < 0)
            return null;

        var startQuote = texto.IndexOf('"', colon + 1);
        if (startQuote < 0)
            return null;

        var end = -1;
        for (var i = startQuote + 1; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (ch == '\\' && i + 1 < texto.Length) { i++; continue; }
            if (ch == '"')
            {
                var j = i + 1;
                while (j < texto.Length && char.IsWhiteSpace(texto[j])) j++;
                if (j >= texto.Length || texto[j] == ',' || texto[j] == '}')
                {
                    end = i;
                    break;
                }
            }
        }
        if (end < 0) end = texto.Length;

        var valor = texto.Substring(startQuote + 1, end - startQuote - 1);
        valor = DesescaparJsonString(valor);

        if (string.IsNullOrWhiteSpace(valor))
            return null;

        return new ResultadoBriefing
        {
            Briefing = valor,
            AssetsReservados = ExtrairArrayHeuristico(texto, "assets_reservados"),
            ImagensNecessarias = ExtrairArrayHeuristico(texto, "imagens_necessarias")
        };
    }

    private static string DesescaparJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    default: sb.Append(s[i + 1]); break;
                }
                i++;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static List<string> ExtrairArrayHeuristico(string texto, string campo)
    {
        var resultado = new List<string>();
        var key = $"\"{campo}\"";
        var idx = texto.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return resultado;

        var abreCol = texto.IndexOf('[', idx + key.Length);
        if (abreCol < 0) return resultado;

        var fechaCol = texto.IndexOf(']', abreCol + 1);
        if (fechaCol < 0) return resultado;

        var inner = texto.Substring(abreCol + 1, fechaCol - abreCol - 1);
        var emAspas = false;
        var inicio = -1;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '"' && (i == 0 || inner[i - 1] != '\\'))
            {
                if (!emAspas) { emAspas = true; inicio = i + 1; }
                else { resultado.Add(DesescaparJsonString(inner.Substring(inicio, i - inicio))); emAspas = false; }
            }
        }
        return resultado;
    }

    private static List<string> ExtrairListaStrings(JsonElement root, string campo)
    {
        var lista = new List<string>();
        if (root.TryGetProperty(campo, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var valor = item.GetString();
                if (!string.IsNullOrEmpty(valor))
                    lista.Add(valor);
            }
        }
        return lista;
    }
}
