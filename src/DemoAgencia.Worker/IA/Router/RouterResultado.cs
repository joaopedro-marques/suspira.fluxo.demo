namespace DemoAgencia.Worker.IA.Router;

public record RouterResultado(
    string Tipo,
    string? Resposta,
    List<string> Perguntas,
    string? Cliente,
    Brief? Brief,
    string? Motivo = null)
{
    public static class Motivos
    {
        public const string AssuntoForaEscopo = "assunto_fora_escopo";
        public const string ClienteNaoPermitido = "cliente_nao_permitido";
        public const string CanalNaoPermitido = "canal_nao_permitido";
    }
}
