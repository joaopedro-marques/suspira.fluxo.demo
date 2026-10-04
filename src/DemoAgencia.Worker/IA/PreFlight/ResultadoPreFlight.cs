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
    public List<string>? Perguntas { get; init; }
    public string? MensagemBloqueio { get; init; }
    public List<string> AssetsReservados { get; init; } = new();
    public string? Cliente { get; init; }

    public static ResultadoPreFlight Concluido(string briefing, List<string> assetsReservados, string? cliente)
        => new() { Tipo = TipoResultadoPreFlight.Concluido, Briefing = briefing, AssetsReservados = assetsReservados, Cliente = cliente };

    public static ResultadoPreFlight PrecisaEsclarecimento(List<string> perguntas)
        => new() { Tipo = TipoResultadoPreFlight.PrecisaEsclarecimento, Perguntas = perguntas };

    public static ResultadoPreFlight Bloqueado(string mensagem)
        => new() { Tipo = TipoResultadoPreFlight.Bloqueado, MensagemBloqueio = mensagem };

    public static ResultadoPreFlight Falha()
        => new() { Tipo = TipoResultadoPreFlight.Falha };
}
