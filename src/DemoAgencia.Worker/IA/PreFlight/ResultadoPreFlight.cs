namespace DemoAgencia.Worker.IA.PreFlight;

public enum TipoResultadoPreFlight
{
    Concluido,
    PrecisaEsclarecimento,
    Bloqueado,
    Falha
}

public class ResultadoPreFlight
{
    public TipoResultadoPreFlight Tipo { get; init; }
    public string? Briefing { get; init; }
    public string? MensagemOriginal { get; init; }
    public List<string>? Perguntas { get; init; }
    public string? MensagemBloqueio { get; init; }
    public string? Cliente { get; init; }

    public static ResultadoPreFlight Concluido(string briefing, string? mensagemOriginal, string? cliente)
        => new() { Tipo = TipoResultadoPreFlight.Concluido, Briefing = briefing, MensagemOriginal = mensagemOriginal, Cliente = cliente };

    public static ResultadoPreFlight PrecisaEsclarecimento(List<string> perguntas)
        => new() { Tipo = TipoResultadoPreFlight.PrecisaEsclarecimento, Perguntas = perguntas };

    public static ResultadoPreFlight Bloqueado(string mensagem)
        => new() { Tipo = TipoResultadoPreFlight.Bloqueado, MensagemBloqueio = mensagem };

    public static ResultadoPreFlight Falha()
        => new() { Tipo = TipoResultadoPreFlight.Falha };
}
