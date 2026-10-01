using System.Text.RegularExpressions;

namespace DemoAgencia.Worker.Seguranca;

public class AnonimizadorService
{
    private readonly IConfiguration _configuration;

    public AnonimizadorService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public virtual string Anonimizar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;

        var anonimizacaoAtiva = _configuration.GetValue<bool>("Seguranca:AnonimizarDados", true);
        if (!anonimizacaoAtiva)
            return texto;

        texto = AnonimizarEmails(texto);
        texto = AnonimizarTelefones(texto);
        texto = AnonimizarCPF(texto);
        texto = AnonimizarCNPJ(texto);
        texto = AnonimizarCartaoCredito(texto);
        texto = AnonimizarTokens(texto);
        texto = AnonimizarURLsComChaves(texto);

        return texto;
    }

    private static string AnonimizarEmails(string texto)
    {
        return Regex.Replace(texto, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "[email]");
    }

    private static string AnonimizarTelefones(string texto)
    {
        texto = Regex.Replace(texto, @"\(\d{2}\)\s*\d{4,5}-?\d{4}", "[telefone]");
        texto = Regex.Replace(texto, @"\+\d{2}\s*\d{2}\s*\d{4,5}-?\d{4}", "[telefone]");
        texto = Regex.Replace(texto, @"\d{2}\s*\d{4,5}-?\d{4}", "[telefone]");
        return texto;
    }

    private static string AnonimizarCPF(string texto)
    {
        return Regex.Replace(texto, @"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", "[cpf]");
    }

    private static string AnonimizarCNPJ(string texto)
    {
        return Regex.Replace(texto, @"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b", "[cnpj]");
    }

    private static string AnonimizarCartaoCredito(string texto)
    {
        texto = Regex.Replace(texto, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", "[cartao]");
        return texto;
    }

    private static string AnonimizarTokens(string texto)
    {
        texto = Regex.Replace(texto, @"sk-or-[a-zA-Z0-9_-]+", "[token]");
        texto = Regex.Replace(texto, @"pk-lf-[a-zA-Z0-9_-]+", "[token]");
        texto = Regex.Replace(texto, @"sk-lf-[a-zA-Z0-9_-]+", "[token]");
        texto = Regex.Replace(texto, @"Bearer\s+[a-zA-Z0-9._-]+", "Bearer [token]");
        return texto;
    }

    private static string AnonimizarURLsComChaves(string texto)
    {
        return Regex.Replace(texto, @"https?://[^\s]+[?&](api_key|key|token|secret|password)=[^\s&]+", "[url_com_chave]");
    }
}
