using System.Diagnostics.CodeAnalysis;

namespace DemoAgencia.Worker.IA.OrquestradorLoop;

[ExcludeFromCodeCoverage]
public class LoopContext
{
    public long ChatId { get; init; }
    public string Mensagem { get; init; } = string.Empty;
    public string? MensagemOriginal { get; set; }
    public ResultadoPipeline Resultado { get; } = new();
    public Func<string, Task>? OnProgresso { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public string? Cliente { get; set; }
    public string? Entregavel { get; set; }
    public string? UltimoAgente { get; set; }
    public string? UltimoOutputAgente { get; set; }
    public bool QaExecutado { get; set; }
    public bool QaAprovado { get; set; }
    public string? FeedbackQa { get; set; }
    public int RefacoesQa { get; set; }
    public int Turnos { get; set; }
    public string? UltimaAcaoHash { get; set; }

    public int MaxTurnos { get; set; } = 24;
    public int MaxRefacoesQa { get; set; } = 2;
    public int MaxRetriesGratis { get; set; } = 4;
    public int RetriesGratis { get; set; }

    public List<Artefato> Artefatos { get; } = new();
    public List<ItemDeckImagem> ImagensDeck { get; } = new();
    public List<string> PlanoDeck { get; } = new();

    public void AdicionarArtefato(Artefato artefato)
    {
        Artefatos.Add(artefato);
    }

    public void AdicionarImagemNoDeck(ItemDeckImagem item)
    {
        ImagensDeck.Add(item);
    }

    public void SubstituirImagemNoDeck(int indice, ItemDeckImagem item)
    {
        ImagensDeck[indice] = item;
    }
}
