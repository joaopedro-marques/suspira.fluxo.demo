using System.Text.RegularExpressions;
using DemoAgencia.Worker.Configuracoes;
using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.Seguranca;

public partial class AnonimizadorService
{
    private readonly SegurancaOptions _options;

    public AnonimizadorService(IOptions<SegurancaOptions> options)
    {
        _options = options.Value;
    }

    public virtual string Anonimizar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;

        if (!_options.AnonimizarDados)
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

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\(\d{2}\)\s*\d{4,5}-?\d{4}")]
    private static partial Regex TelefoneRegex1();

    [GeneratedRegex(@"\+\d{2}\s*\d{2}\s*\d{4,5}-?\d{4}")]
    private static partial Regex TelefoneRegex2();

    [GeneratedRegex(@"\d{2}\s*\d{4,5}-?\d{4}")]
    private static partial Regex TelefoneRegex3();

    [GeneratedRegex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b")]
    private static partial Regex CpfRegex();

    [GeneratedRegex(@"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b")]
    private static partial Regex CnpjRegex();

    [GeneratedRegex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b")]
    private static partial Regex CartaoRegex();

    [GeneratedRegex(@"sk-or-[a-zA-Z0-9_-]+")]
    private static partial Regex TokenOpenRouterRegex();

    [GeneratedRegex(@"pk-lf-[a-zA-Z0-9_-]+")]
    private static partial Regex TokenLangfusePublicRegex();

    [GeneratedRegex(@"sk-lf-[a-zA-Z0-9_-]+")]
    private static partial Regex TokenLangfuseSecretRegex();

    [GeneratedRegex(@"Bearer\s+[a-zA-Z0-9._-]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"https?://[^\s]+[?&](api_key|key|token|secret|password)=[^\s&]+")]
    private static partial Regex UrlComChaveRegex();

    private static string AnonimizarEmails(string texto) => EmailRegex().Replace(texto, "[email]");

    private static string AnonimizarTelefones(string texto)
    {
        texto = TelefoneRegex1().Replace(texto, "[telefone]");
        texto = TelefoneRegex2().Replace(texto, "[telefone]");
        texto = TelefoneRegex3().Replace(texto, "[telefone]");
        return texto;
    }

    private static string AnonimizarCPF(string texto) => CpfRegex().Replace(texto, "[cpf]");

    private static string AnonimizarCNPJ(string texto) => CnpjRegex().Replace(texto, "[cnpj]");

    private static string AnonimizarCartaoCredito(string texto) => CartaoRegex().Replace(texto, "[cartao]");

    private static string AnonimizarTokens(string texto)
    {
        texto = TokenOpenRouterRegex().Replace(texto, "[token]");
        texto = TokenLangfusePublicRegex().Replace(texto, "[token]");
        texto = TokenLangfuseSecretRegex().Replace(texto, "[token]");
        texto = BearerRegex().Replace(texto, "Bearer [token]");
        return texto;
    }

    private static string AnonimizarURLsComChaves(string texto) => UrlComChaveRegex().Replace(texto, "[url_com_chave]");
}
