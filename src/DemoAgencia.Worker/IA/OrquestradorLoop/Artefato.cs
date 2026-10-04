namespace DemoAgencia.Worker.IA.OrquestradorLoop;

public enum TipoArtefato
{
    Copy,
    Html,
    Imagem,
    Spec,
    Prompt,
    Outro
}

public class Artefato
{
    public string Id { get; init; } = string.Empty;
    public TipoArtefato Tipo { get; init; }
    public string Agente { get; init; } = string.Empty;
    public string Entregavel { get; init; } = string.Empty;
    public string? Notas { get; init; }
    public string Resumo { get; init; } = string.Empty;
    public int Tamanho => Entregavel.Length;

    private static int _contador;

    public static Artefato Criar(TipoArtefato tipo, string agente, string entregavel, string resumo, string? notas = null)
    {
        var id = $"art_{Interlocked.Increment(ref _contador)}";
        return new Artefato
        {
            Id = id,
            Tipo = tipo,
            Agente = agente,
            Entregavel = entregavel,
            Notas = notas,
            Resumo = resumo
        };
    }
}
