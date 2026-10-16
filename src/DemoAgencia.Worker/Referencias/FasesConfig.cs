namespace DemoAgencia.Worker.Referencias;

public class FasesConfig
{
    public Dictionary<string, FaseDefinicao> Fases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FaseDefinicao
{
    public List<string> Sinonimos { get; set; } = new();
    
    public bool Matches(string inputNormalized)
    {
        if (string.IsNullOrWhiteSpace(inputNormalized))
            return false;
        
        // Check exact match with phase key
        if (inputNormalized.Equals(FaseKey, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check contains match with phase key
        if (inputNormalized.Contains(FaseKey, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check all synonyms
        foreach (var sinonimo in Sinonimos)
        {
            var sinNormalized = NormalizarTexto(sinonimo);
            if (inputNormalized.Contains(sinNormalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
    
    public string FaseKey { get; set; } = string.Empty;
    
    private static string NormalizarTexto(string texto)
    {
        return texto.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i").Replace("ó", "o").Replace("ô", "o")
            .Replace("ú", "u").Replace("ã", "a").Replace("õ", "o")
            .Replace("ç", "c").Replace(" ", "-").Replace("_", "-");
    }
}
